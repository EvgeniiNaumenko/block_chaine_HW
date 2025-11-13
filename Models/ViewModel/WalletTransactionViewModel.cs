namespace BlockChain_FP_ITStep.Models.ViewModel
{
    public class WalletTransactionViewModel
    {
        public Transaction Tx { get; set; } = null!;
        public int? BlockIndex { get; set; }  // null = pending
    }
}
