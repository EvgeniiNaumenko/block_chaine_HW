using System.Security.Cryptography;
using System.Text;

namespace BlockChain_FP_ITStep.Models
{
    public class Wallet
    {
        public string Address { get; set; } = string.Empty;
        public string PublicKeyXml { get; set; } = string.Empty;
        public string DisplayName {  get; set; } = string.Empty; 
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public static string DereveAddressFromPublicKeyXml(string publicKeyXml)
        {
            if (string.IsNullOrWhiteSpace(publicKeyXml))
                throw new ArgumentException("Public key XML cannot be null or empty");

            var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(publicKeyXml));
            var hex20 = BitConverter.ToString(hash, 0, 20).Replace("-", "");
            return "ADDR_" + hex20;
        }

    }
}
