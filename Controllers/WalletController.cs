using BlockChain_FP_ITStep.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlockChain_FP_ITStep.Controllers
{
    public class WalletController : Controller
    {
        private readonly BlockChainService _bcService;

        public WalletController(BlockChainService bcService)
        {
            _bcService = bcService;
        }

        // Wallet details page
        [HttpGet("/wallet/{address}")]
        public async Task<IActionResult> Index(string address, string nodeId = "A")
        {
            if (string.IsNullOrWhiteSpace(address))
                return BadRequest("Wallet address required");

            var balances = await _bcService.GetBalances(nodeId, includeMempool: true);
            balances.TryGetValue(address, out var balance);

            // Все транзакции кошелька (из блоков + mempool)
            var txs = await _bcService.GetTransactionsByWalletAsync(address, nodeId);

            var wallet = _bcService.Wallets.ContainsKey(address)
                ? _bcService.Wallets[address]
                : null;

            // Индекс последнего блока для высчитвания confirmations(1/6..) у транзакции
            var blocks = await _bcService.GetAllBlocksAsync(nodeId);
            ViewBag.LastBlockIndex = blocks.Any() ? blocks.Max(b => b.Index) : 0;

            ViewBag.Address = address;
            ViewBag.Balance = balance;
            ViewBag.Wallet = wallet;

            return View(txs);
        }


    }
}

