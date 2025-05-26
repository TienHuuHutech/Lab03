using Lab03.Models;
using Lab03.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Lab03.Controllers
{
    public class ShoppingCartController : Controller
    {
        private readonly IBookRepository _bookRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public ShoppingCartController(
            IBookRepository bookRepository,
            ICategoryRepository categoryRepository,
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager)
        {
            _bookRepository = bookRepository;
            _categoryRepository = categoryRepository;
            _context = context;
            _userManager = userManager;
        }

        public IActionResult Index()
        {
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemDto>>("Cart") ?? new List<CartItemDto>();
            if (!cart.Any())
            {
                return View("EmptyCart");
            }

            return View(cart); // Truyền List<CartItemDto> sang View
        }

        [HttpPost]
        public async Task<IActionResult> AddToCart(int id, int quantity)
        {
            if (quantity <= 0)
                quantity = 1;

            var book = await _bookRepository.GetByIdAsync(id);
            if (book == null) return NotFound();

            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemDto>>("Cart") ?? new List<CartItemDto>();

            var existingItem = cart.FirstOrDefault(i => i.BookID == book.ID);
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                cart.Add(new CartItemDto
                {
                    BookID = book.ID,
                    Title = book.Title,
                    Price = (decimal)book.Price,
                    Quantity = quantity
                });
            }

            HttpContext.Session.SetObjectAsJson("Cart", cart);
            return RedirectToAction("Index");
        }

        public IActionResult RemoveItem(int id)
        {
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemDto>>("Cart") ?? new List<CartItemDto>();
            var itemToRemove = cart.FirstOrDefault(i => i.BookID == id);
            if (itemToRemove != null)
            {
                cart.Remove(itemToRemove);
                HttpContext.Session.SetObjectAsJson("Cart", cart);
            }

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Checkout()
        {
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemDto>>("Cart");
            if (cart == null || !cart.Any())
            {
                return RedirectToAction("Index");
            }

            var order = new Order
            {
                TotalPrice = cart.Sum(i => i.Price * i.Quantity)
            };

            return View(order);
        }


        [HttpPost]
        public async Task<IActionResult> Checkout(Order order)
        {
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemDto>>("Cart");
            if (cart == null || !cart.Any())
            {
                return RedirectToAction("Index");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // ✅ Lấy phương thức thanh toán từ form
            var paymentMethod = Request.Form["PaymentMethod"]; // e.g., "COD" hoặc "BankTransfer"

            order.UserId = user.Id;
            order.OrderDate = DateTime.UtcNow;
            order.TotalPrice = cart.Sum(i => i.Price * i.Quantity);
            order.OrderDetails = cart.Select(i => new OrderDetail
            {
                BookId = i.BookID,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToList();

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            HttpContext.Session.Remove("Cart");

            // ✅ Truyền paymentMethod qua TempData để hiển thị ở trang OrderCompleted
            TempData["PaymentMethod"] = paymentMethod;

            return View("OrderCompleted", order.Id);
        }
        [HttpPost]
        public IActionResult UpdateQuantity(int bookId, int quantity)
        {
            if (quantity < 1)
                return BadRequest("Quantity must be at least 1.");

            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemDto>>("Cart") ?? new List<CartItemDto>();
            var item = cart.FirstOrDefault(i => i.BookID == bookId);

            if (item == null)
                return NotFound();

            item.Quantity = quantity;
            HttpContext.Session.SetObjectAsJson("Cart", cart);

            var itemTotal = item.Price * item.Quantity;
            var cartTotal = cart.Sum(i => i.Price * i.Quantity);

            return Json(new
            {
                success = true,
                itemTotal = itemTotal.ToString("N0"),
                cartTotal = cartTotal.ToString("N0")
            });
        }

    }
}
