using Lab03.Models;
using Lab03.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKit.Net.Smtp;
namespace Lab03.Controllers
{
    public class ShoppingCartController : Controller
    {
        private readonly IBookRepository _bookRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly EmailSettings _emailSettings;

        public ShoppingCartController(
            IBookRepository bookRepository,
            ICategoryRepository categoryRepository,
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IOptions<EmailSettings> emailSettings)
        {
            _bookRepository = bookRepository;
            _categoryRepository = categoryRepository;
            _context = context;
            _userManager = userManager;
            _emailSettings = emailSettings.Value;
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

            await SendOrderConfirmationEmail(user.Email, order, paymentMethod);

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
        private async Task SendOrderConfirmationEmail(string toEmail, Order order, string paymentMethod)
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = $"Xác nhận đơn hàng #{order.Id} tại BookStore";

            var bodyBuilder = new BodyBuilder();

            bodyBuilder.HtmlBody = $@"
            <h3>Đơn hàng của bạn đã được đặt thành công!</h3>
            <p>Mã đơn hàng: <strong>{order.Id}</strong></p>
            <p>Ngày đặt: {order.OrderDate:dd/MM/yyyy HH:mm}</p>
            <p>Phương thức thanh toán: {paymentMethod}</p>
            <p>Tổng tiền: {order.TotalPrice.ToString("N0")} đ</p>
            <h4>Chi tiết đơn hàng:</h4>
            <ul>
                {string.Join("", order.OrderDetails.Select(d => $"<li>Book ID: {d.BookId}, Số lượng: {d.Quantity}, Giá: {d.Price.ToString("N0")} đ</li>"))}
            </ul>
            <p>Chúng tôi sẽ liên hệ và giao hàng đến địa chỉ: {order.ShippingAddress}</p>
            <p>Cảm ơn bạn đã mua hàng tại BookStore!</p>
        ";

            email.Body = bodyBuilder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.SenderPassword);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}
