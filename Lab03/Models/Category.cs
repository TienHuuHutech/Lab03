using System.ComponentModel.DataAnnotations;

namespace Lab03.Models
{
    public class Category
    {
        public int ID { get; set; }
        [Required, StringLength(50)]
        public string Name { get; set; }
        public List<Book> Books { get; set; }
    }
}
