namespace Lab03.Models
{
    public class CartItem
    {
        public int BookID { get; set; }
        public string Title { get; set; }
        public decimal Price => (decimal)Book.Price * Quantity;
        public int Quantity { get; set; }
        public Book Book { get; set; }
    }
}
