using Lab03.Models;
using Lab03.Repositories;
using Lab03.VnPay;
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
        private readonly VnPaySettings _vnPaySettings;

        public ShoppingCartController(
            IBookRepository bookRepository,
            ICategoryRepository categoryRepository,
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IOptions<EmailSettings> emailSettings,
            IOptions<VnPaySettings> vnPayOptions)
        {
            _bookRepository = bookRepository;
            _categoryRepository = categoryRepository;
            _context = context;
            _userManager = userManager;
            _emailSettings = emailSettings.Value;
            _vnPaySettings = vnPayOptions.Value;
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

            // Lấy dữ liệu từ form
            var fullName = Request.Form["FullName"];
            var phone = Request.Form["Phone"];
            var shippingAddress = order.ShippingAddress;
            var paymentMethod = Request.Form["PaymentMethod"];

            // Gán thông tin đơn hàng
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
            await _context.SaveChangesAsync(); // có Order.Id

            // ✅ Nếu chọn VNPAY → Redirect tới cổng thanh toán
            if (paymentMethod == "VNPAY")
            {
                var vnPay = new VnPayLibrary();
                var amount = (long)(order.TotalPrice * 100); // VNPAY dùng x100
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

                vnPay.AddRequestData("vnp_Version", "2.1.0");
                vnPay.AddRequestData("vnp_Command", "pay");
                vnPay.AddRequestData("vnp_TmnCode", _vnPaySettings.TmnCode);             // dùng IOptions<VnPaySettings>
                vnPay.AddRequestData("vnp_Amount", amount.ToString());
                vnPay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
                vnPay.AddRequestData("vnp_CurrCode", "VND");
                vnPay.AddRequestData("vnp_IpAddr", ipAddress);
                vnPay.AddRequestData("vnp_Locale", "vn");
                vnPay.AddRequestData("vnp_OrderInfo", $"Thanh toán đơn hàng #{order.Id}");
                vnPay.AddRequestData("vnp_OrderType", "other");
                vnPay.AddRequestData("vnp_ReturnUrl", _vnPaySettings.ReturnUrl);
                vnPay.AddRequestData("vnp_TxnRef", order.Id.ToString());

                string paymentUrl = vnPay.CreateRequestUrl(_vnPaySettings.Url, _vnPaySettings.HashSecret);
                return Redirect(paymentUrl);
            }

            // ✅ Nếu COD → Gửi email và hiển thị trang xác nhận
            order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Book)
                .FirstOrDefaultAsync(o => o.Id == order.Id);

            await SendPendingOrderEmail(
                user.Email!,
                order,
                fullName,
                phone,
                user.Email!,
                shippingAddress
            );

            HttpContext.Session.Remove("Cart");
            TempData["PaymentMethod"] = paymentMethod;

            return View("OrderCompleted", order.Id);
        }

        public async Task<IActionResult> VnPayReturn()
        {
            var vnpay = new VnPayLibrary();
            foreach (var (key, value) in Request.Query)
            {
                if (key.StartsWith("vnp_"))
                    vnpay.AddResponseData(key, value);
            }

            var orderId = int.Parse(vnpay.GetResponseData("vnp_TxnRef"));
            var responseCode = vnpay.GetResponseData("vnp_ResponseCode");
            var secureHash = Request.Query["vnp_SecureHash"];

            var isValid = vnpay.ValidateSignature(secureHash, _vnPaySettings.HashSecret);
            if (!isValid)
            {
                ViewBag.Message = "Chữ ký không hợp lệ";
                return View("PaymentFailed");
            }

            if (responseCode == "00")
            {
                // ✅ Nạp đơn hàng và thông tin liên quan
                var order = await _context.Orders
                    .Include(o => o.OrderDetails)
                    .ThenInclude(d => d.Book)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null)
                {
                    ViewBag.Message = "Không tìm thấy đơn hàng.";
                    return View("PaymentFailed");
                }

                // ✅ Lấy người dùng từ Identity
                var user = await _userManager.FindByIdAsync(order.UserId);

                if (user == null)
                {
                    ViewBag.Message = "Không tìm thấy người dùng.";
                    return View("PaymentFailed");
                }

                // ✅ Gửi email xác nhận đã thanh toán
                await SendPendingOrderEmail(
                    user.Email!,
                    order,
                    user.UserName!,
                    user.PhoneNumber ?? "",
                    user.Email!,
                    order.ShippingAddress
                );

                TempData["PaymentMethod"] = "VNPAY";
                return View("OrderCompleted", orderId);
            }

            ViewBag.Message = $"Thanh toán không thành công. Mã lỗi: {responseCode}";
            return View("PaymentFailed");
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