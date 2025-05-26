namespace Lab03.Models
{
    public class CartItemDto
    {
        public int BookID { get; set; }
        public string Title { get; set; }
        public decimal Price { get; set; } // đơn giá
        public int Quantity { get; set; }
    }

}
