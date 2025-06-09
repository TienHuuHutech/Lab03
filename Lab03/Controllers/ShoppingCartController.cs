using Lab03.Models;
using Lab03.Repositories;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

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

        public IActionResult Checkout()
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
                return RedirectToAction("Index");

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login", "Account");

            // ✅ Lấy thông tin người nhận từ form
            var fullName = Request.Form["FullName"];
            var phone = Request.Form["Phone"];
            var shippingAddress = order.ShippingAddress;
            var paymentMethod = Request.Form["PaymentMethod"];

            // ✅ Gán thông tin đơn hàng
            order.UserId = user.Id;
            order.OrderDate = DateTime.UtcNow;
            order.TotalPrice = cart.Sum(i => i.Price * i.Quantity);
            order.ShippingAddress = shippingAddress;
            order.Notes = order.Notes;
            order.OrderDetails = cart.Select(i => new OrderDetail
            {
                BookId = i.BookID,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToList();

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // ✅ Nạp Book
            order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Book)
                .FirstOrDefaultAsync(o => o.Id == order.Id);

            // ✅ Gửi email bằng email từ IdentityUser
            await SendPendingOrderEmail(
                user.Email!,
                order,
                fullName,
                phone,
                user.Email!, // không cần lấy từ form
                shippingAddress
            );

            HttpContext.Session.Remove("Cart");
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

        private async Task SendPendingOrderEmail(string toEmail, Order order, string customerName, string phone, string email, string diaChi)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = $"Xác nhận đơn hàng #{order.Id} - Chờ thanh toán";

            var bodyBuilder = new BodyBuilder();

            // Đọc template từ file
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "send2.html");
            var htmlTemplate = await System.IO.File.ReadAllTextAsync(templatePath);

            // Xử lý danh sách sản phẩm
            var sanPhamHtml = string.Join("", order.OrderDetails.Select(d =>
                $@"<tr>
            <td style='color:#636363;border:1px solid #e5e5e5;padding:12px;text-align:left'>{d.Book.Title}</td>
            <td style='color:#636363;border:1px solid #e5e5e5;padding:12px;text-align:left'>{d.Quantity}</td>
            <td style='color:#636363;border:1px solid #e5e5e5;padding:12px;text-align:left'>{d.Price.ToString("N0")} ₫</td>
        </tr>"));

            // Thay thế các placeholder
            var htmlBody = htmlTemplate
                .Replace("{{TenKhachHang}}", customerName)
                .Replace("{{MaDon}}", order.Id.ToString())
                .Replace("{{NgayDatHang}}", order.OrderDate.ToString("dd/MM/yyyy HH:mm"))
                .Replace("{{SanPham}}", sanPhamHtml)
                .Replace("{{ThanhTien}}", order.TotalPrice.ToString("N0"))
                .Replace("{{TongTien}}", order.TotalPrice.ToString("N0"))
                .Replace("{{DiaChi}}", diaChi)
                .Replace("{{Phone}}", phone)
                .Replace("{{Email}}", email);

            bodyBuilder.HtmlBody = htmlBody;
            message.Body = bodyBuilder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.SenderPassword);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);
        }
    }
}