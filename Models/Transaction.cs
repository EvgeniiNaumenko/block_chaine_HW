using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace BlockChain_FP_ITStep.Models
{
    public class Transaction
    {
        [Key]
        public int Id { get; set; }
        public int BlockId { get; set; }
        public string NodeId { get; set; } = "A";
        [Required]
        public string FromAddress { get; set; } = string.Empty;
        [Required]
        public string ToAddress { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public string Signature { get; set; } = string.Empty;
        public string? Note { get; set; }

        // nav prop
        public Block Block { get; set; }

        // Метод унификации строки, которую будем использовать в транзакциях? и также потом как часть Подписи?
        public string CanonicalPayload()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2:0.########}|{3:0.########}", FromAddress, ToAddress, Amount, Fee);
        }
    }
}