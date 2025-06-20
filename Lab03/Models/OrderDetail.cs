﻿namespace Lab03.Models
{
    public class OrderDetail
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int BookId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public Order Order { get; set; }
        public Book Book { get; set; }
    }
}
