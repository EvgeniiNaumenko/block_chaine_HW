namespace BlockChain_FP_ITStep.Models
{
    public class BlockValidationViewModel
    {
        public Block Block { get; set; } = null!;
        public bool IsValid { get; set; }               // состояние цепочки
        public bool IsSignatureValid { get; set; }      // подпись RSA
    }
}
