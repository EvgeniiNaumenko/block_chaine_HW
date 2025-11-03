using BlockChain_FP_ITStep.Models;
using BlockChain_FP_ITStep.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlockChain_FP_ITStep.Controllers
{
    public class BlockChainController(BlockChainService bcService) : Controller
    {
        private readonly BlockChainService _bcService = bcService;

        public static CancellationTokenSource? _cts;

        public async Task<IActionResult> Index()
        {
            ViewBag.AlertMessage = TempData["AlertMessage"];
            ViewBag.AlertType = TempData["AlertType"];

            var validatedBlocks = await _bcService.GetValidatedBlocksAsync();
            var isSignatureValid = await _bcService.GetSignatureValidationAsync();

            var model = validatedBlocks.Select((block, i) => new BlockValidationViewModel
            {
                Block = block.Block,
                IsValid = block.IsValid,                        // цепочка
                IsSignatureValid = isSignatureValid[i].IsValid  // подпись
            }).ToList();

            ViewBag.IsChainValid = model.All(b => b.IsValid);
            ViewBag.Difficulty = BlockChainService.Difficulty;

            // TODO Add Public key to View ?
            ViewBag.MempoolCount = _bcService.Mempool.Count;
            ViewBag.Mempool = _bcService.Mempool;
            ViewBag.Wallets = _bcService.Wallets.Values.ToList();
            return View(model);
        }


        // Убрать после удаления ручного добавленяи блоков в UI
        //[HttpPost]
        //public async Task<IActionResult> Add(string data, string privateKey)
        //{
        //    if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(privateKey))
        //    {
        //        TempData["AlertMessage"] = "Please enter both data and private key.";
        //        TempData["AlertType"] = "danger";
        //        return RedirectToAction(nameof(Index));
        //    }

        //    try
        //    {
        //        long ms = await _bcService.AddBlockAsync(data, privateKey);
        //        TempData["AlertMessage"] = "Block successfully added.";
        //        TempData["AlertType"] = "success";
        //    }
        //    catch (Exception ex)
        //    {
        //        TempData["AlertMessage"] = "Error: " + ex.Message;
        //        TempData["AlertType"] = "danger";
        //    }

        //    return RedirectToAction("Index");
        //}


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
        public async Task<IActionResult> SearchByHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return RedirectToAction(nameof(Index));

            var blocks = await _bcService.GetAllBlocksAsync();
            var found = blocks.FirstOrDefault(b => b.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase));    //  Ordinal -> Сравнивает побайтово символы, без учёта языка и культуры.  + IgnoreCase

            if (found == null)
            {
                ViewBag.SearchMessage = "Block not found.";
                ViewBag.IsChainValid = await _bcService.IsValidAsync();
                var validatedBlocks = await _bcService.GetValidatedBlocksAsync();
                return View("Index", validatedBlocks);
            }

            return View(found);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var block = await _bcService.GetBlockByIdAsync(id);
            if (block == null) return NotFound();
            return View(block);
        }

        [HttpPost]
        public IActionResult SetDifficulty(int difficulty)
        {
            if (difficulty < 1) difficulty = 1;
            if (difficulty > 6) difficulty = 6;
            BlockChainService.Difficulty =  difficulty;
            return RedirectToAction("Index");
        }

        // Mining
        [HttpPost]
        public IActionResult StartMining(string privateKey)
        {
            if (string.IsNullOrWhiteSpace(privateKey))
                return BadRequest("Private key required");

            _cts = new CancellationTokenSource();
            var progress = new Progress<int>(_ => { });

            Task.Run(async () =>
            {
                await _bcService.MineAsync(privateKey, _cts.Token, progress);
            });

            return Ok();
        }

        [HttpPost]
        public IActionResult StopMining()
        {
            _cts?.Cancel();
            return Ok();
        }

        [HttpPost]
        public IActionResult RegisterWallet(string publicKeyXml, string displayName)
        {
            var wallet = _bcService.RegisterWallet(publicKeyXml, displayName);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult CreateTransaction(string fromAddress, string toAddress, decimal amount, decimal fee, string privateKey, string note)
        {
            var tx = new Models.Transaction
            {
                FromAddress = fromAddress,
                ToAddress = toAddress,
                Amount = amount,
                Fee = fee,
                Note = note
            };

            tx.Signature = BlockChainService.SignPayload(tx.CanonicalPayload(), privateKey);

            try
            {
                _bcService.CreateTransaction(tx);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> MinePending(string privateKey)
        {
            try
            {
                await _bcService.MinePendingAsync(privateKey);
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult DemoSetup()
        {
            var (Ivan,  prvKey)  = _bcService.CreateWallet("Ivan");
            var (Taras, prvKey2) = _bcService.CreateWallet("Taras");

            decimal amount = 10.0m;
            decimal fee = 0.5m;

            var tx = new Models.Transaction
            {
                FromAddress = Ivan.Address,
                ToAddress = Taras.Address,
                Amount = amount,
                Fee = fee,
                Note = "Payment for services"
            };

            var sig = BlockChainService.SignPayload(tx.CanonicalPayload(), prvKey);

            tx.Signature = sig;

            _bcService.CreateTransaction(tx);

            return RedirectToAction("Index");
        }


    }
}
