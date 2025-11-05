using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace BlockChain_FP_ITStep.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        // Адрес отправителя
        [Required]
        public string FromAddress { get; set; } = string.Empty;

        // Адрес получателя
        [Required]
        public string ToAddress { get; set; } = string.Empty;

        // Сумма перевода
        public decimal Amount { get; set; }

        public Block Block { get; set; } = null;
        public int BlockId { get; set; }

        // Комиссия за транзакцию
        public decimal Fee { get; set; }

        // Цифровая подпись (подтверждает подлинность транзакции)
        public string Signature { get; set; } = string.Empty;

        // Необязательное примечание или комментарий к транзакции
        public string? Note { get; set; }


        // Метод создаёт стандартную ("каноническую") строку для подписи и проверки транзакции
        // Формат: "From|To|Amount|Fee"
        public string CanonicalPayload()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2:0.########}|{3:0.########}",
                FromAddress, ToAddress, Amount, Fee
            );
        }
    }
}
