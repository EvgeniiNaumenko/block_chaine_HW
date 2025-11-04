using System.Security.Cryptography;
using System.Text;

namespace BlockChain_FP_ITStep.Models
{
    public class Wallet
    {
        // Уникальный адрес кошелька (генерируется на основе публичного ключа)
        public string Address { get; set; } = string.Empty;

        // Публичный ключ в XML-формате (RSA)
        public string PublicKeyXml { get; set; } = string.Empty;

        // Имя, отображаемое в интерфейсе (может быть имя пользователя, псевдоним и т.п.)
        public string DisplayName { get; set; } = string.Empty;

        // Время регистрации кошелька (по умолчанию текущее UTC-время)
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;


        // Метод для генерации адреса на основе публичного ключа (XML)
        public static string DereveAddressFromPublicKeyXml(string publicKeyXml)
        {
            // Проверяем, что ключ не пустой
            if (string.IsNullOrWhiteSpace(publicKeyXml))
                throw new ArgumentException("Public key XML cannot be null or empty");

            // Создаём SHA256-хешер
            var sha = SHA256.Create();

            // Вычисляем хеш от XML-строки публичного ключа
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(publicKeyXml));

            // Берём первые 20 байт хеша и переводим в HEX-строку
            var hex20 = BitConverter.ToString(hash, 0, 20).Replace("-", "");

            // Возвращаем адрес в читаемом формате
            return "ADDR_" + hex20;
        }
    }
}
