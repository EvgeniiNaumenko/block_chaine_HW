using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;

namespace BlockChain_FP_ITStep.Models
{
    public class Block
    {
        [Key]
        public int Id { get; set; }  // Первичный ключ БД (не участвует в вычислении хэша)

        public int Index { get; set; }  // Номер блока в цепочке (влияет на порядок и участвует в хэше)

        public List<Transaction> Transactions { get; } = new();  // Список транзакций, входящих в блок

        // Количество транзакций — удобно для вывода в интерфейсе
        public int TxCount => Transactions.Count;

        public string PrevHash { get; set; }  // Хэш предыдущего блока
        public string Hash { get; set; }      // Текущий хэш блока
        public DateTime Timestamp { get; set; }  // Время создания блока

        public string Signature { get; private set; } = "";  // Подпись блока владельцем
        public string PublicKeyXml { get; private set; }     // Публичный ключ, связанный с подписью

        // Данные для Proof of Work (PoW)
        public int Nonce { get; set; }          // Число, изменяемое при майнинге
        public int Difficulty { get; set; }     // Сложность (кол-во нулей в начале хэша)
        public long MiningDurationMs { get; set; }  // Время, затраченное на майнинг (в миллисекундах)


        // Пустой конструктор для EF Core
        public Block() { }

        // Конструктор для создания нового блока
        public Block(int index, string prevHash)
        {
            Index = index;
            PrevHash = prevHash;
            Timestamp = DateTime.UtcNow;
            Hash = ComputeHash();  // Вычисляем хэш сразу при создании
        }


        // Устанавливает список транзакций в блок
        public void SetTransaction(List<Transaction> transactions)
        {
            Transactions.Clear();
            Transactions.AddRange(transactions);
        }


        // Приводим все транзакции к одной строке (для хэша)
        private string CanonicalizerTransactions()
        {
            var sb = new StringBuilder();
            foreach (var tx in Transactions)
            {
                sb.Append(tx.CanonicalPayload());
                sb.Append("|");  // Разделяем транзакции символом “|”
            }
            return sb.ToString();
        }


        // Основной метод вычисления хэша блока
        public string ComputeHash()
        {
            // Округляем Timestamp до миллисекунд (SQL может обрезать точность)
            var ts = new DateTime(
                Timestamp.Ticks - (Timestamp.Ticks % TimeSpan.TicksPerMillisecond),
                DateTimeKind.Utc
            );

            // Объединяем все важные данные блока в одну строку
            var raw = Index + PrevHash + ts.ToString("O") + Nonce + Difficulty + CanonicalizerTransactions();

            // Вычисляем SHA256
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));

            // Возвращаем строку без дефисов
            return BitConverter.ToString(bytes).Replace("-", "");
        }


        // Подписываем блок приватным ключом
        public void Sign(RSAParameters privateKey, string publicKey)
        {
            var rsa = RSA.Create();
            rsa.ImportParameters(privateKey);

            byte[] data = Encoding.UTF8.GetBytes(Hash);
            byte[] sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            Signature = Convert.ToBase64String(sig);  // Сохраняем подпись в Base64
            PublicKeyXml = publicKey;                 // Запоминаем публичный ключ
        }


        // Проверяем подпись блока (валидация)
        public bool Verify()
        {
            if (string.IsNullOrWhiteSpace(Signature)) return false;

            try
            {
                var rsa = RSA.Create();
                rsa.FromXmlString(PublicKeyXml);

                byte[] data = Encoding.UTF8.GetBytes(Hash);
                byte[] sig = Convert.FromBase64String(Signature);

                return rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return false;
            }
        }


        // Позволяет обновить подпись вручную (редко используется)
        public void UpdateSignature(string newSignature)
        {
            Signature = newSignature;
        }


        // Алгоритм майнинга (PoW) — ищем хэш, начинающийся с N нулей
        public void Mine(int difficulty)
        {
            Difficulty = difficulty;
            string target = new string('0', Difficulty);

            var sw = Stopwatch.StartNew();
            do
            {
                Nonce++;
                Hash = ComputeHash();
            }
            while (!Hash.StartsWith(target, StringComparison.Ordinal));

            sw.Stop();
            MiningDurationMs = sw.ElapsedMilliseconds;
        }


        // Проверка валидности блока (хэш и сложность)
        public bool HashValidProof()
        {
            string target = new string('0', Difficulty);
            return Hash == ComputeHash() && Hash.StartsWith(target, StringComparison.Ordinal);
        }
    }
}
