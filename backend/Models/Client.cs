namespace backend.Models;

public class Client
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<Request> Request { get; set; } = new List<Request>();
}