using BlockChain_FP_ITStep.Models;
using BlockChain_FP_ITStep.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlockChain_FP_ITStep.Controllers
{
    public class BlockChainController(BlockChainService bcService) : Controller
    {
        private readonly BlockChainService _bcService = bcService;

        // Источник токена отмены — используется для остановки майнинга
        public static CancellationTokenSource? _cts;


        // ====================== ГЛАВНАЯ СТРАНИЦА ======================
        public async Task<IActionResult> Index()
        {
            // Сообщения об успехе / ошибке через TempData
            ViewBag.AlertMessage = TempData["AlertMessage"];
            ViewBag.AlertType = TempData["AlertType"];

            // Получаем список валидированных блоков и проверку подписей
            var validatedBlocks = await _bcService.GetValidatedBlocksAsync();
            var isSignatureValid = await _bcService.GetSignatureValidationAsync();

            // Формируем модель для отображения в Index.cshtml
            var model = validatedBlocks.Select((block, i) => new BlockValidationViewModel
            {
                Block = block.Block,
                IsValid = block.IsValid,
                IsSignatureValid = isSignatureValid[i].IsValid
            }).ToList();

            // Проверяем целостность цепочки
            ViewBag.IsChainValid = model.All(b => b.IsValid);
            ViewBag.Difficulty = BlockChainService.Difficulty;

            // Mempool — непопавшие в блок транзакции
            ViewBag.MempoolCount = _bcService.Mempool.Count;
            ViewBag.Mempool = _bcService.Mempool;

            // Кошельки и балансы
            ViewBag.Wallets = _bcService.Wallets.Values.ToList();
            ViewBag.Balances = await _bcService.GetBalancesAsync(includeMempool: true);

            return View(model);
        }


        // ====================== ГЕНЕРАЦИЯ КЛЮЧЕЙ ======================

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


        // ====================== РЕДАКТИРОВАНИЕ БЛОКОВ ======================

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


        // ====================== УДАЛЕНИЕ БЛОКОВ ======================

        [HttpPost]
        public async Task<IActionResult> DeleteBlock(int index)
        {
            try
            {
                bool result = await _bcService.DeleteBlockAsync(index);

                if (result)
                {
                    TempData["AlertMessage"] = $"✅ Блок с индексом {index} успешно удалён.";
                    TempData["AlertType"] = "success";
                }
                else
                {
                    TempData["AlertMessage"] = $"❌ Блок с индексом {index} не найден.";
                    TempData["AlertType"] = "danger";
                }
            }
            catch (Exception ex)
            {
                TempData["AlertMessage"] = $"⚠️ Ошибка при удалении блока: {ex.Message}";
                TempData["AlertType"] = "danger";
            }

            return RedirectToAction(nameof(Index));
        }


        // ====================== ДЕТАЛИ БЛОКА ======================

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var block = await _bcService.GetBlockByIdAsync(id);
            if (block == null) return NotFound();
            return View(block);
        }


        // ====================== СЛОЖНОСТЬ МАЙНИНГА ======================

        [HttpPost]
        public IActionResult SetDifficulty(int difficulty)
        {
            if (difficulty < 1) difficulty = 1;
            if (difficulty > 6) difficulty = 6;
            BlockChainService.Difficulty = difficulty;
            return RedirectToAction("Index");
        }


        // ====================== МАЙНИНГ ======================

        [HttpPost]
        public IActionResult StartMining(string privateKey)
        {
            if (string.IsNullOrWhiteSpace(privateKey))
                return BadRequest("Private key required");

            _cts = new CancellationTokenSource();
            var progress = new Progress<int>(_ => { });

            // Запускаем майнинг в отдельном потоке
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


        // ====================== РЕГИСТРАЦИЯ КОШЕЛЬКА ======================

        [HttpPost]
        public IActionResult RegisterWallet(string publicKeyXml, string displayName)
        {
            var wallet = _bcService.RegisterWallet(publicKeyXml, displayName);
            return RedirectToAction("Index");
        }


        // ====================== СОЗДАНИЕ ТРАНЗАКЦИИ ======================

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

            // Подписываем транзакцию
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


        // ====================== МАЙНИНГ НЕПОДТВЕРЖДЕННЫХ ТРАНЗАКЦИЙ ======================

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


        // ====================== DEMO НАСТРОЙКА ======================

        [HttpPost]
        public async Task<IActionResult> DemoSetup()
        {
            // Создаем два кошелька
            var (User1, prvKey) = _bcService.CreateWallet("Ivan");
            var (User2, prvey2) = _bcService.CreateWallet("Taras");
            // Майним 15 раз для каждого кошелька
            for (int i = 0; i < 15; i++)
            {
               MinePending(prvKey);
                MinePending(prvey2);
            }
            // Перевод между кошельками
            decimal amount = 5.0m;
            decimal fee = 0.5m;

            var tx = new Models.Transaction
            {
                FromAddress = User1.Address,
                ToAddress = User2.Address,
                Amount = amount,
                Fee = fee,
                Note = "Payment for services"
            };

            // Подпись транзакции
            tx.Signature = BlockChainService.SignPayload(tx.CanonicalPayload(), prvKey);
            _bcService.CreateTransaction(tx);

            return RedirectToAction("Index");
        }

    }
}
