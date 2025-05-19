using System.ComponentModel.DataAnnotations;

namespace Lab03.Models
{
    public class Book
    {
        public int ID { get; set; }
        [Required, StringLength(50)]
        public string Title { get; set; }
        public string Author { get; set; }
        public int PublishYear { get; set; }
        public double Price { get; set; }
        public string Cover { get; set; }
        public List<BookImage> Images { get; set; } 
        public int CategoryID { get; set; }
        public string? Description { get; set; }
        public Category? Category { get; set; }
    }
}