using BlockChain_FP_ITStep.Data;
using BlockChain_FP_ITStep.Hubs;
using BlockChain_FP_ITStep.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace BlockChain_FP_ITStep.Services
{
    public class BlockChainService
    {
        // Фабрика контекста БД (используется для потокобезопасного создания DbContext)
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        // Контекст SignalR для отправки данных в реальном времени на фронтенд
        private readonly IHubContext<MiningHub> _hub;

        // Глобальная сложность майнинга (кол-во нулей в начале хэша)
        public static int Difficulty { get; set; } = 3;

        // Зарегистрированные кошельки (адрес -> объект Wallet)
        public Dictionary<string, Wallet> Wallets { get; set; } = new();

        // Список неподтверждённых транзакций
        public List<Transaction> Mempool { get; set; } = new();

        // Вознаграждение майнера за блок
        public const decimal MinerReward = 1.0m;


        // Конструктор сервиса
        public BlockChainService(IDbContextFactory<ApplicationDbContext> dbFactory, IHubContext<MiningHub> hub)
        {
            _dbFactory = dbFactory;
            _hub = hub;

            // При первом запуске — создаём генезис-блок (если его нет)
            using var db = _dbFactory.CreateDbContext();
            InitGenBlock(db);
        }

        // Метод создания генезис-блока (нулевого)
        private void InitGenBlock(ApplicationDbContext db)
        {
            if (!db.Blocks.Any())
            {
                using var rsa = RSA.Create(2048);                // Генерация пары ключей
                var privateKey = rsa.ExportParameters(true);
                var publicKeyXml = rsa.ToXmlString(false);

                var genBlock = new Block(0, "");                 // Первый блок (нет предыдущего хэша)
                genBlock.Sign(privateKey, publicKeyXml);         // Подписываем блок

                db.Blocks.Add(genBlock);                         // Сохраняем в базу
                db.SaveChanges();
            }
        }

        // Регистрация нового кошелька (добавление в словарь)
        public Wallet RegisterWallet(string publicKeyXml, string displayName)
        {
            var wallet = new Wallet
            {
                PublicKeyXml = publicKeyXml,
                Address = Wallet.DereveAddressFromPublicKeyXml(publicKeyXml),
                DisplayName = displayName
            };
            Wallets[wallet.Address] = wallet;
            return wallet;
        }

        // Создание новой транзакции (и проверка её подписи)
        public async void CreateTransaction(Transaction transaction)
        {
            var rsa = RSA.Create();
            var wallet = Wallets[transaction.FromAddress];       // Получаем кошелёк отправителя
            rsa.FromXmlString(wallet.PublicKeyXml);

            // Проверяем подпись транзакции
            var payload = Encoding.UTF8.GetBytes(transaction.CanonicalPayload());
            var sig = Convert.FromBase64String(transaction.Signature);

            if (!rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new Exception("Invalid Transaction Signature");



            var balances = await GetBalancesAsync(true);
            if (!balances.TryGetValue(transaction.FromAddress, out var fromBal))
                fromBal = 0;
            var requiredAmount = transaction.Amount + transaction.Fee;
            if (fromBal < requiredAmount)
                throw new Exception("insufficient balance");

            // Добавляем в мемпул
            Mempool.Add(transaction);
        }

        // Асинхронный майнинг блока с неподтверждёнными транзакциями
        public async Task<Block> MinePendingAsync(string privateKeyXml)
        {
            using var db = _dbFactory.CreateDbContext();
            var prevBlock = await db.Blocks.OrderBy(b => b.Index).LastOrDefaultAsync();   // Предыдущий блок

            // Из приватного ключа получаем публичный
            using var rsa = RSA.Create();
            rsa.FromXmlString(privateKeyXml);
            var publicKeyXml = rsa.ToXmlString(false);

            // Определяем адрес майнера по публичному ключу
            var minerAddress = Wallets.Values.FirstOrDefault(w => w.PublicKeyXml == publicKeyXml)?.Address;

            // Суммируем комиссии всех транзакций
            decimal totalFee = Mempool.Sum(t => t.Fee);

            // Первая транзакция — награда майнеру (COINBASE)
            var txs = new List<Transaction>
            {
                new Transaction
                {
                    FromAddress = "COINBASE",
                    ToAddress = minerAddress,
                    Amount = MinerReward + totalFee
                }
            };

            // Добавляем обычные транзакции из мемпула
            txs.AddRange(Mempool);

            // Создаём новый блок
            var newBlock = new Block(prevBlock.Index + 1, prevBlock.Hash);
            newBlock.SetTransaction(txs);

            // Запускаем процесс майнинга (Proof-of-Work)
            newBlock.Mine(Difficulty);

            // Подписываем блок приватным ключом
            var privateParams = rsa.ExportParameters(true);
            newBlock.Sign(privateParams, publicKeyXml);

            // Сохраняем блок в БД
            db.Blocks.Add(newBlock);
            await db.SaveChangesAsync();

            // Очищаем мемпул
            Mempool.Clear();
            return newBlock;
        }

        // Добавление нового блока в цепочку
        public async Task<long> AddBlockAsync(string data, string privateKeyXml)
        {
            try
            {
                var blocks = await GetAllBlocksAsync();
                var prevBlock = blocks.LastOrDefault();
                if (prevBlock == null) return 0;

                using var db = _dbFactory.CreateDbContext();

                // Проверяем, что на этой позиции блока ещё нет
                var exists = await db.Blocks.AnyAsync(b => b.Index == blocks.Count);
                if (exists)
                    throw new InvalidOperationException("Block position conflict");

                var newBlock = new Block(blocks.Count, prevBlock.Hash);

                // Майним блок
                newBlock.Mine(Difficulty);

                // Проверяем корректность ключей
                var publicKeyXml = GetPublicKeyFromPrivate(privateKeyXml);
                if (string.IsNullOrEmpty(publicKeyXml))
                    throw new CryptographicException("Key format invalid");

                // Подписываем блок
                using var rsa = RSA.Create();
                rsa.FromXmlString(privateKeyXml);
                var privateParams = rsa.ExportParameters(true);
                newBlock.Sign(privateParams, publicKeyXml);

                // Сохраняем блок
                db.Blocks.Add(newBlock);
                await db.SaveChangesAsync();
                return newBlock.MiningDurationMs;
            }
            catch (CryptographicException)
            {
                throw new ApplicationException("Invalid private key. Please try again with a valid key.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("fork"))
            {
                throw new ApplicationException("Block rejected: chain fork detected. Refresh chain.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AddBlockAsync] {ex.GetType().Name}: {ex.Message}");
                throw new ApplicationException("Unexpected error during block creation.");
            }
        }

        // Получение всех блоков
        public async Task<List<Block>> GetAllBlocksAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Blocks.OrderBy(b => b.Index).Include(x=>x.Transactions).ToListAsync();
        }

        // Удаление блока по индексу (осторожно!)
        public async Task<bool> DeleteBlockAsync(int index)
        {
            using var db = _dbFactory.CreateDbContext();
            var block = await db.Blocks.FirstOrDefaultAsync(b => b.Index == index);
            if (block == null)
                return false;

            db.Blocks.Remove(block);
            await db.SaveChangesAsync();
            return true;
        }

        // Поиск блока по индексу
        public async Task<Block?> GetBlockByIndexAsync(int index)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Blocks.FirstOrDefaultAsync(b => b.Index == index);
        }

        // Поиск блока по Id
        public async Task<Block?> GetBlockByIdAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Blocks.FirstOrDefaultAsync(b => b.Id == id);
        }

        // Редактирование блока (например, обновление подписи)
        public async Task<bool> EditBlockAsync(int index, string? signature = null)
        {
            using var db = _dbFactory.CreateDbContext();
            var block = await db.Blocks.FirstOrDefaultAsync(b => b.Index == index);
            if (block == null) return false;

            // Если передана подпись — обновляем
            if (!string.IsNullOrWhiteSpace(signature))
                block.UpdateSignature(signature);

            // Пересчитываем хэш
            block.Hash = block.ComputeHash();

            db.Blocks.Update(block);
            await db.SaveChangesAsync();
            return true;
        }

        // Проверка всей цепочки на целостность
        public async Task<bool> IsValidAsync()
        {
            var blocks = await GetAllBlocksAsync();

            for (int i = 1; i < blocks.Count; i++)
            {
                var current = blocks[i];
                var prevBlock = blocks[i - 1];

                // Проверка связности и подписи
                if (current.PrevHash != prevBlock.Hash) return false;
                if (current.Hash != current.ComputeHash()) return false;
                if (!current.Verify()) return false;
                if (!current.HashValidProof()) return false;
            }
            return true;
        }

        // Возвращает список блоков с отметкой валидности
        public async Task<List<BlockValidationViewModel>> GetValidatedBlocksAsync()
        {
            var blocks = await GetAllBlocksAsync();
            var result = new List<BlockValidationViewModel>();
            bool stillValid = true;

            for (int i = 0; i < blocks.Count; i++)
            {
                bool isValid = true;

                if (i > 0)
                {
                    var prev = blocks[i - 1];
                    if (stillValid)
                    {
                        if (blocks[i].PrevHash != prev.Hash || !blocks[i].Verify())
                        {
                            stillValid = false;
                            isValid = false;
                        }
                    }
                    else
                        isValid = false; // Все после повреждённого блока — невалидны
                }

                result.Add(new BlockValidationViewModel
                {
                    Block = blocks[i],
                    IsValid = isValid
                });
            }

            return result;
        }

        // Генерация приватного ключа (в формате XML)
        public string GeneratePrivateKeyXml()
        {
            using var rsa = RSA.Create();
            return rsa.ToXmlString(true); // true — экспорт всей пары (публичный + приватный)
        }

        // Получение публичного ключа из приватного
        public string? GetPublicKeyFromPrivate(string privateKeyXml)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(privateKeyXml);
                return rsa.ToXmlString(false); // false — только публичная часть
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetPublicKeyFromPrivate] Invalid key: {ex.Message}");
                return null;
            }
        }

        // Проверка подписи блока (валид/невалид)
        public async Task<List<BlockValidationViewModel>> GetSignatureValidationAsync()
        {
            var blocks = await GetAllBlocksAsync();
            return blocks.Select(b => new BlockValidationViewModel
            {
                Block = b,
                IsValid = b.Verify()
            }).ToList();
        }

        // Асинхронный процесс майнинга блока с SignalR обновлениями
        public async Task<long> MineAsync(string privateKeyXml, CancellationToken ct, IProgress<int>? progress = null)
        {
            var blocks = await GetAllBlocksAsync();
            var prevBlock = blocks.Last();

            var newBlock = new Block(blocks.Count, prevBlock.Hash)
            {
                Difficulty = Difficulty
            };

            string target = new string('0', Difficulty); // строка-цель ("000")
            var sw = Stopwatch.StartNew();
            int tries = 0;
            long attemptCounter = 0;
            var rateTimer = Stopwatch.StartNew();

            // Цикл подбора Nonce до тех пор, пока не найдём подходящий хэш
            while (!ct.IsCancellationRequested)
            {
                newBlock.Nonce++;
                newBlock.Hash = newBlock.ComputeHash();
                tries++;
                attemptCounter++;

                // Отправляем прогресс каждые 5000 итераций
                if (tries % 5000 == 0)
                {
                    int percent = Math.Min(99, tries / 20000);
                    progress?.Report(percent);
                    await _hub.Clients.All.SendAsync("MiningProgress", percent);
                }

                // Раз в секунду — обновляем скорость майнинга
                if (rateTimer.ElapsedMilliseconds >= 1000)
                {
                    await _hub.Clients.All.SendAsync("MiningAttemptsPerSecond", attemptCounter);
                    attemptCounter = 0;
                    rateTimer.Restart();
                }

                // Проверка, найден ли нужный хэш
                if (newBlock.Hash.StartsWith(target))
                {
                    sw.Stop();
                    newBlock.MiningDurationMs = sw.ElapsedMilliseconds;

                    // Подписываем найденный блок
                    using var rsa = RSA.Create();
                    rsa.FromXmlString(privateKeyXml);
                    var privateParams = rsa.ExportParameters(true);
                    var publicKeyXml = rsa.ToXmlString(false);
                    newBlock.Sign(privateParams, publicKeyXml);

                    // Сохраняем блок
                    using var db = _dbFactory.CreateDbContext();
                    db.Blocks.Add(newBlock);
                    await db.SaveChangesAsync();

                    // Завершаем SignalR обновления
                    await _hub.Clients.All.SendAsync("MiningProgress", 100);
                    await _hub.Clients.All.SendAsync("MiningAttemptsPerSecond", 0);

                    return newBlock.MiningDurationMs;
                }
            }

            // Если майнинг прерван пользователем
            await _hub.Clients.All.SendAsync("MiningProgress", -1);
            await _hub.Clients.All.SendAsync("MiningAttemptsPerSecond", 0);
            return -1;
        }

        // Создание демо-кошелька (для теста)
        public (Wallet wallet, string privateKeyXml) CreateWallet(string displayName)
        {
            var rsa = RSA.Create();
            var privateKeyXml = rsa.ToXmlString(true);
            var publicKeyXml = rsa.ToXmlString(false);
            var wallet = RegisterWallet(publicKeyXml, displayName);
            return (wallet, privateKeyXml);
        }

        // Подпись произвольной строки приватным ключом
        public static string SignPayload(string payload, string privateKeyXml)
        {
            var rsa = RSA.Create();
            rsa.FromXmlString(privateKeyXml);
            var data = Encoding.UTF8.GetBytes(payload);
            var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(sig);
        }

        // Получение балансов всех кошельков
        public async Task<Dictionary<string, decimal>> GetBalancesAsync(bool includeMempool = false)
        {
            var balances = new Dictionary<string, decimal>();
            var blocks = await GetAllBlocksAsync();

            // Проходим по всем блокам и суммируем транзакции
            foreach (var block in blocks)
            {
                foreach (var tran in block.Transactions)
                {
                    ApplyTransactionToBallaces(balances, tran);
                }
            }

            // При необходимости — добавляем транзакции из мемпула
            if (includeMempool)
            {
                foreach (var tran in Mempool)
                {
                    ApplyTransactionToBallaces(balances, tran);
                }
            }

            return balances;
        }

        // Применяет одну транзакцию к текущим балансам
        private static void ApplyTransactionToBallaces(Dictionary<string, decimal> balances, Transaction tx)
        {
            // Добавляем сумму получателю
            if (!balances.TryGetValue(tx.ToAddress, out var toBal))
                toBal = 0;
            balances[tx.ToAddress] = toBal + tx.Amount;

            // Снимаем сумму с отправителя (если это не Coinbase)
            if (tx.FromAddress != "COINBASE")
            {
                if (!balances.TryGetValue(tx.FromAddress, out var fromBal))
                    fromBal = 0;
                balances[tx.FromAddress] = fromBal - (tx.Amount + tx.Fee);
            }
        }
    }
}
