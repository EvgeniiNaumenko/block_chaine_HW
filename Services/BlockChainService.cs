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
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly IHubContext<MiningHub> _hub;    // MiningHub (SignalR)
        public static int Difficulty { get; set; } = 3;
       

        public Dictionary<string, Wallet> Wallets { get; set; } = new();
        public List<Transaction> Mempool { get; set; } = new();
        public const decimal MinerReward = 1.0m;



        public BlockChainService(IDbContextFactory<ApplicationDbContext> dbFactory, IHubContext<MiningHub> hub)
        {
            _dbFactory = dbFactory;
            _hub = hub;

            using var db = _dbFactory.CreateDbContext();
            InitGenBlock(db);
        }

        private void InitGenBlock(ApplicationDbContext db)
        {
            if (!db.Blocks.Any())
            {
                using var rsa = RSA.Create(2048);
                var privateKey = rsa.ExportParameters(true);
                var publicKeyXml = rsa.ToXmlString(false);

                var genBlock = new Block(0, "");
                genBlock.Sign(privateKey, publicKeyXml);

                db.Blocks.Add(genBlock);
                db.SaveChanges();
            }
        }
        

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

        public void CreateTransaction(Transaction transaction)
        {
            var rsa = RSA.Create();
            var wallet = Wallets[transaction.FromAddress];
            rsa.FromXmlString(wallet.PublicKeyXml);
            var payload = Encoding.UTF8.GetBytes(transaction.CanonicalPayload());
            var sig = Convert.FromBase64String(transaction.Signature);
            if(!rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                throw new Exception("Invalid Transaction Signature");
            }
            Mempool.Add(transaction);
        }

        public async Task<Block> MinePendingAsync(string privateKeyXml)
        {
            using var db = _dbFactory.CreateDbContext();
            var prevBlock = await db.Blocks.OrderBy(b => b.Index).LastAsync();

            // Получаем публичный ключ фром private
            using var rsa = RSA.Create();
            rsa.FromXmlString(privateKeyXml);
            var publicKeyXml = rsa.ToXmlString(false);

            var minerAddress = Wallets.Values.FirstOrDefault(w => w.PublicKeyXml == publicKeyXml)?.Address;
            decimal totalFee = Mempool.Sum(t => t.Fee);
            var txs = new List<Transaction>
            {
                new Transaction
                {
                    FromAddress = "COINBASE",
                    ToAddress = minerAddress,
                    Amount = MinerReward + totalFee
                }
            };

            txs.AddRange(Mempool);
            var newBlock = new Block(prevBlock.Index + 1, prevBlock.Hash);
            newBlock.SetTransaction(txs);
            newBlock.Mine(Difficulty);

            var privateParams = rsa.ExportParameters(true);
            newBlock.Sign(privateParams, publicKeyXml);

            db.Blocks.Add(newBlock);
            await db.SaveChangesAsync();

            Mempool.Clear();
            return newBlock;
        }


        public async Task<long> AddBlockAsync(string data, string privateKeyXml)
        {
            try
            {
                var blocks = await GetAllBlocksAsync();
                var prevBlock = blocks.LastOrDefault();
                if (prevBlock == null) return 0;

                using var db = _dbFactory.CreateDbContext();

                // Проверка что блок на этой позиции ещё не существует
                var exists = await db.Blocks.AnyAsync(b => b.Index == blocks.Count);
                if (exists)
                {
                    throw new InvalidOperationException("Block position conflict");
                }

                var newBlock = new Block(blocks.Count, prevBlock.Hash);

                // Mining
                newBlock.Mine(Difficulty);

                // Key validation
                var publicKeyXml = GetPublicKeyFromPrivate(privateKeyXml);
                if (string.IsNullOrEmpty(publicKeyXml))
                    throw new CryptographicException("Key format invalid");

                using var rsa = RSA.Create();
                rsa.FromXmlString(privateKeyXml);
                var privateParams = rsa.ExportParameters(true);

                newBlock.Sign(privateParams, publicKeyXml);

                // Save
                db.Blocks.Add(newBlock);
                await db.SaveChangesAsync();
                return newBlock.MiningDurationMs;
            }
            catch (CryptographicException)
            {
                throw new ApplicationException("Invalid private key. Please try again with a valid key.");
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("fork"))
            {
                throw new ApplicationException("Block rejected: chain fork detected. Refresh chain.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AddBlockAsync] {ex.GetType().Name}: {ex.Message}");
                throw new ApplicationException("Unexpected error during block creation.");
            }
        }

        public async Task<List<Block>> GetAllBlocksAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Blocks.OrderBy(b => b.Index).ToListAsync();
        }

        public async Task<Block?> GetBlockByIndexAsync(int index)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Blocks.FirstOrDefaultAsync(b => b.Index == index);
        }

        public async Task<Block?> GetBlockByIdAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Blocks.FirstOrDefaultAsync(b => b.Id == id);
        }

        public async Task<bool> EditBlockAsync(int index, string? signature = null)
        {
            using var db = _dbFactory.CreateDbContext();

            var block = await db.Blocks.FirstOrDefaultAsync(b => b.Index == index);
            if (block == null) return false;

            if (!string.IsNullOrWhiteSpace(signature))
            {
                block.UpdateSignature(signature);
            }
            block.Hash = block.ComputeHash();

            db.Blocks.Update(block);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> IsValidAsync()
        {
            var blocks = await GetAllBlocksAsync();

            for (int i = 1; i < blocks.Count; i++)
            {
                var current = blocks[i];
                var prevBlock = blocks[i - 1];

                if (current.PrevHash != prevBlock.Hash) return false;
                if (current.Hash != current.ComputeHash()) return false;
                if (!current.Verify()) return false;
                if (!current.HashValidProof()) return false;
            }
            return true;
        }

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
                    {
                        isValid = false; // всё после повреждённого блока — не валидно
                    }
                }

                result.Add(new BlockValidationViewModel
                {
                    Block = blocks[i],
                    IsValid = isValid
                });
            }

            return result;
        }

        public string GeneratePrivateKeyXml()
        {
            using var rsa = RSA.Create();
            return rsa.ToXmlString(true);      // true = экспортировать всю пару ключей(публичный + приватный компоненты)
        }

        public string? GetPublicKeyFromPrivate(string privateKeyXml)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(privateKeyXml);   // может упасть, если не XML
                return rsa.ToXmlString(false);      // fase  экспортируем только открытую часть
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetPublicKeyFromPrivate] Invalid key: {ex.Message}");
                return null;
            }
        }

        // Валидация сигнатуры для valid/invalid signature в Index() 
        public async Task<List<BlockValidationViewModel>> GetSignatureValidationAsync()
        {
            var blocks = await GetAllBlocksAsync();
            return blocks.Select(b => new BlockValidationViewModel
            {
                Block = b,
                IsValid = b.Verify()
            }).ToList();
        }

        public async Task<long> MineAsync(string privateKeyXml, CancellationToken ct, IProgress<int>? progress = null)
        {
            var blocks = await GetAllBlocksAsync();         // получаем текущую цепочку
            var prevBlock = blocks.Last();
            var newBlock = new Block(blocks.Count, prevBlock.Hash)
            {
                Difficulty = Difficulty
            };

            string target = new string('0', Difficulty);    // строка вида "000"
            var sw = Stopwatch.StartNew();
            int tries = 0;

            // attempts/sec (перебор Nonce)
            long attemptCounter = 0;
            var rateTimer = Stopwatch.StartNew();

            while (!ct.IsCancellationRequested)
            {
                newBlock.Nonce++;
                newBlock.Hash = newBlock.ComputeHash();
                tries++;
                attemptCounter++;

                // Обновляем прогресс каждые 5000 попыток (чтобы не спамить signalr)
                if (tries % 5000 == 0)
                {
                    int percent = Math.Min(99, tries / 20000);
                    progress?.Report(percent);
                    await _hub.Clients.All.SendAsync("MiningProgress", percent);
                }

                // отправка attempts/sec раз в секунду
                if (rateTimer.ElapsedMilliseconds >= 1000)
                {
                    await _hub.Clients.All.SendAsync("MiningAttemptsPerSecond", attemptCounter);
                    attemptCounter = 0;
                    rateTimer.Restart();
                }

                // нужный хэш найден
                if (newBlock.Hash.StartsWith(target))
                {
                    sw.Stop();
                    newBlock.MiningDurationMs = sw.ElapsedMilliseconds;

                    // подписываем блок
                    using var rsa = RSA.Create();
                    rsa.FromXmlString(privateKeyXml);
                    var privateParams = rsa.ExportParameters(true);
                    var publicKeyXml = rsa.ToXmlString(false);
                    newBlock.Sign(privateParams, publicKeyXml);

                    using var db = _dbFactory.CreateDbContext();
                    db.Blocks.Add(newBlock);
                    await db.SaveChangesAsync();

                    await _hub.Clients.All.SendAsync("MiningProgress", 100);
                    await _hub.Clients.All.SendAsync("MiningAttemptsPerSecond", 0);

                    return newBlock.MiningDurationMs;
                }
            }

            // Если майнинг остановлен
            await _hub.Clients.All.SendAsync("MiningProgress", -1);
            await _hub.Clients.All.SendAsync("MiningAttemptsPerSecond", 0);
            return -1;
        }


        // Demo Method, later be remooved...
        public (Wallet wallet, string privateKeyXml) CreateWallet(string displayName)
        {
            var rsa = RSA.Create();
            var privateKeyXml = rsa.ToXmlString(true);
            var publicKeyXml = rsa.ToXmlString(false);
            var wallet = RegisterWallet(publicKeyXml, displayName);
            return(wallet, privateKeyXml);
        }

        public static string SignPayload(string payload, string privateKeyXml)
        {
            var rsa = RSA.Create();
            rsa.FromXmlString(privateKeyXml);
            var data = Encoding.UTF8.GetBytes(payload);
            var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(sig);
        }



    }
}
