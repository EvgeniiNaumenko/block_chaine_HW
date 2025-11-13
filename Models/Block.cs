using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;

namespace BlockChain_FP_ITStep.Models
{
    public class Block
    {
        [Key]
        public int Id { get; set; }         // Id - первичный ключ БД (не влияет на хэш)

        public int Index { get; set; }      // Index - номер блока в цепочке, участвует в хэше и определяет порядок блоков
        public string? NodeId { get; set; }

        // Кол-во транзакций для UI.
        public int TxCount => Transactions.Count;
        public string PrevHash { get; set; }
        public string Hash { get; set; }
        public DateTime Timestamp { get; set; }

        public string? Signature { get; private set; } = "";   // Подпись блока // у генезис блока нету подписи.
        public string? PublicKeyXml { get; private set; }

        // l3 -> PoW
        public int Nonce { get; set; }
        public int Difficulty { get; set; }
        public long MiningDurationMs { get; set; }


        public List<Transaction> Transactions { get; set; } = new();


        // -------------------- //
        public Block() { }

        public Block(int index, string prevHash)
        {
            Index = index;
            PrevHash = prevHash;
            Timestamp = DateTime.UtcNow;
            Hash = ComputeHash();
        }

        public Block(int index, string prevHash, DateTime dateTime)
        {
            Index = index;
            PrevHash = prevHash;
            Timestamp = dateTime;
            Hash = ComputeHash();
        }

        public void SetTransaction(List<Transaction> transactions)
        {
            Transactions.Clear();
            Transactions.AddRange(transactions);
        }

        private string CanonicalizerTransactions()
        {
            var sb = new StringBuilder();
            foreach (var tx in Transactions)
            {
                sb.Append(tx.CanonicalPayload());
                sb.Append("|");
            }
            return sb.ToString();
        }


        public string ComputeHash()
        {
            var ts = new DateTime(Timestamp.Ticks - (Timestamp.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc); // Округляем время до миллисекунд, чтобы совпадало с точностью SQL и хэш не менялся после сохранения

            //var raw = Index + Data + PrevHash + ts.ToString("O") + Nonce + Difficulty;
            var raw = Index + PrevHash + ts.ToString("O") + Nonce + Difficulty + CanonicalizerTransactions();

            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        // ====  L2 ===
        public void Sign(RSAParameters privateKey, string publicKey)      // функция подписи
        {
            var rsa = RSA.Create();
            rsa.ImportParameters(privateKey);
            byte[] data = Encoding.UTF8.GetBytes(Hash);
            byte[] sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Signature = Convert.ToBase64String(sig);
            PublicKeyXml = publicKey;
        }

        public bool Verify()
        {
            if (String.IsNullOrWhiteSpace(Signature)) return false;
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

        public void UpdateSignature(string newSignature)
        {
            Signature = newSignature;
        }

        public void Mine(int difficulty)
        {
            Difficulty = difficulty;
            string target = new string('0', Difficulty);

            var sw = Stopwatch.StartNew();
            do
            {
                Nonce++;
                Hash = ComputeHash();
            } while (!Hash.StartsWith(target, StringComparison.Ordinal));

            sw.Stop();
            MiningDurationMs = sw.ElapsedMilliseconds;
        }


        public bool HashValidProof()
        {
            string target = new string('0', Difficulty);
            return Hash == ComputeHash() && Hash.StartsWith(target, StringComparison.Ordinal);
        }

        public void UpdatePublicKey(string publicKeyXml)
        {
            PublicKeyXml = publicKeyXml;
        }


    }
}
