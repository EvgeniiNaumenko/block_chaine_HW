using BlockChain_FP_ITStep.Models;
using BlockChain_FP_ITStep.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlockChain_FP_ITStep.Controllers
{
    public class BlockChainController(BlockChainService bcService) : Controller
    {
        private readonly BlockChainService _bcService = bcService;

        public static CancellationTokenSource? _cts;

        public async Task<IActionResult> Index(string nodeId = "A")
        {
            ViewBag.AlertMessage = TempData["AlertMessage"];
            ViewBag.AlertType = TempData["AlertType"];

            // Валидированные блоки и статус подписи
            var validatedBlocks = await _bcService.GetValidatedBlocksAsync(nodeId);
            var isSignatureValid = await _bcService.GetSignatureValidationAsync(nodeId);

            var model = validatedBlocks.Select((block, i) => new BlockValidationViewModel
            {
                Block = block.Block,
                IsValid = block.IsValid,                            // валидность цепочки
                IsSignatureValid = isSignatureValid[i].IsValid      // валидность подписи
            }).ToList();

            // Добавляем reward для каждого блока
            foreach (var vm in model)
                vm.Reward = _bcService.GetBlockReward(vm.Block.Index);

            // reward для следующего блока
            var lastIndex = model.Max(m => m.Block.Index);
            ViewBag.CurrentReward = _bcService.GetBlockReward(lastIndex + 1);

            ViewBag.IsChainValid = model.All(b => b.IsValid);
            ViewBag.Difficulty = BlockChainService.Difficulty;

            // Mempool и балансы
            ViewBag.Mempool = _bcService.Mempool.Where(t => t.NodeId == nodeId).ToList();
            ViewBag.MempoolCount = ((List<Transaction>)ViewBag.Mempool).Count;
            ViewBag.Wallets = _bcService.Wallets.Values.ToList();
            ViewBag.Balances = await _bcService.GetBalances(nodeId, true);

            // Список нод и текущая нода
            ViewBag.Nodes = await _bcService.GetNodeIdsAsync();
            ViewBag.NodeId = nodeId;

            // ==================== Передаем ключи ноды ====================
            ViewBag.NodePrivateKey = _bcService.GetNodePrivateKey(nodeId);
            ViewBag.NodePublicKey = _bcService.GetNodePublicKey(nodeId);

            return View(model);
        }


        // маршрут для генерации ключа
        [HttpGet]
        public IActionResult GenerateKey()
        {
            var privateKey = _bcService.GeneratePrivateKeyXml();
            if (string.IsNullOrWhiteSpace(privateKey))
                return BadRequest("Error generating key");

            return Content(privateKey, "text/plain");
        }

        [HttpGet]
        public IActionResult GenerateKeyPair()
        {
            var privateKey = _bcService.GeneratePrivateKeyXml();
            var publicKey = _bcService.GetPublicKeyFromPrivate(privateKey);

            if (privateKey == null || publicKey == null)
                return BadRequest("Key generation failed");

            return Json(new { privateKey, publicKey });
        }


        [HttpGet]
        public async Task<IActionResult> Edit(int index)
        {
            var block = await _bcService.GetBlockByIndexAsync(index);
            if (block == null) return NotFound();
            return View(block);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int index, string signature)
        {
            var result = await _bcService.EditBlockAsync(index, signature);
            if (!result) return NotFound();
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> SearchByHash(string hash, string nodeId = "A")
        {
            if (string.IsNullOrWhiteSpace(hash))
                return RedirectToAction(nameof(Index));

            var blocks = await _bcService.GetAllBlocksAsync(nodeId);
            var found = blocks.FirstOrDefault(b => b.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase));    //  Ordinal -> Сравнивает побайтово символы, без учёта языка и культуры.  + IgnoreCase

            if (found == null)
            {
                ViewBag.SearchMessage = "Block not found.";
                ViewBag.IsChainValid = await _bcService.IsValidAsync(nodeId);
                var validatedBlocks = await _bcService.GetValidatedBlocksAsync(nodeId);
                return View("Index", validatedBlocks);
            }

            return View(found);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id, string nodeId = "A")
        {
            var block = await _bcService.GetBlockByIdWithTransactionsAsync(id);
            if (block == null) return NotFound();

            // передаем индекс последнего блока для подсчёта подтверждений
            var blocks = await _bcService.GetAllBlocksAsync(nodeId);
            ViewBag.LastBlockIndex = blocks.Max(b => b.Index);

            return View(block);
        }

        [HttpPost]
        public IActionResult SetDifficulty(int difficulty)
        {
            if (difficulty < 1) difficulty = 1;
            if (difficulty > 6) difficulty = 6;
            BlockChainService.Difficulty = difficulty;
            return RedirectToAction("Index");
        }


        [HttpPost]
        public IActionResult StopMining()
        {
            _cts?.Cancel();
            return Ok();
        }

        [HttpPost]
        public IActionResult RegisterWallet(string publicKeyXml, string displayName, string nodeId)
        {
            _bcService.RegisterWallet(publicKeyXml, displayName);

            return RedirectToAction("Index", new { nodeId });
        }

        [HttpPost]
        public IActionResult CreateTransaction(string fromAddress, string toAddress, decimal amount, decimal fee, string privateKey, string note, string nodeId = "A")
        {
            var tx = new Transaction
            {
                NodeId = nodeId,
                FromAddress = fromAddress,
                ToAddress = toAddress,
                Amount = amount,
                Fee = fee,
                Note = note
            };

            tx.Signature = BlockChainService.SignPayload(tx.CanonicalPayload(), privateKey);

            try
            {
                _bcService.CreateTransaction(tx, nodeId);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { nodeId });
        }

        [HttpPost]
        public async Task<IActionResult> MinePending(string privateKey, string nodeId = "A")
        {
            try
            {
                await _bcService.MinePendingAsync(privateKey, nodeId);
                await _bcService.BroadcastChainAsync(nodeId);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.InnerException?.Message ?? ex.Message;
                return RedirectToAction("Index", new { nodeId });
            }

            return RedirectToAction("Index", new { nodeId });
        }

        [HttpPost]
        public async Task<IActionResult> DemoSetup()
        {
            var (Ivan, prvKey) = _bcService.CreateWallet("Ivan");
            var (Taras, prvKey2) = _bcService.CreateWallet("Taras");
            var nodeId = "A";

            decimal amount = 3.0m;
            decimal fee = 0.1m;

            var tx = new Transaction
            {
                NodeId = nodeId,
                FromAddress = Ivan.Address,
                ToAddress = Taras.Address,
                Amount = amount,
                Fee = fee,
                Note = "Payment for services"
            };

            for (int i = 0; i < 5; i++)
            {
                await MinePending(prvKey, nodeId);
                await MinePending(prvKey2, nodeId);
            }

            var sig = BlockChainService.SignPayload(tx.CanonicalPayload(), prvKey);
            tx.Signature = sig;

            try
            {
                _bcService.CreateTransaction(tx, nodeId);
            }
            catch (Exception)
            {
                TempData["Error"] = "Demo transaction failed.";
                return RedirectToAction("Index", new { nodeId });
            }

            TempData["Success"] = "Demo completed!";
            return RedirectToAction("Index", new { nodeId });
        }



    }
}
