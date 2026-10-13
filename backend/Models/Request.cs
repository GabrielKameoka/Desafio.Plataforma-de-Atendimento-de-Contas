using backend.Models.Enums;

namespace backend.Models;

public class Request
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    
    public Client Client { get; set; }
    public Status Status { get; set; }
    public Priority Priority { get; set; }
    public Attendant Attendant { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime Deadline { get; set; }
}