using System.ComponentModel.DataAnnotations;

namespace Lab03.Models
{
    public class BookImage
    {
        public int ID { get; set; }
        [Required, StringLength(50)]
        public string Url { get; set; }
        public int BookID { get; set; }
        public Book? Book { get; set; }
    }
}
