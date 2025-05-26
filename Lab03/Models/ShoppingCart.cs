using System.ComponentModel.DataAnnotations;

namespace Lab03.Models
{
    public class ShoppingCart
    {
        public Book book { get; set; }
        [Range(0, 10000)]
        public int Count { get; set; }
        public List<CartItem> Items
        { get; set; } = new List<CartItem>();
        public void AddItem(CartItem item)
        {
            var existingItem = Items.FirstOrDefault(i => i.BookID == item.BookID);
            if (existingItem != null)
            {
                existingItem.Quantity += item.Quantity;
            }
            else
            {
                Items.Add(item);
            }
        }
        public void RemoveItem(int bookId)
        {
            Items.RemoveAll(i => i.BookID == bookId);
        }
    }
}
