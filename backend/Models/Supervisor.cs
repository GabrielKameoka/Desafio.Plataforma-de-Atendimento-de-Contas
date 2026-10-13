namespace backend.Models;

public class Supervisor
{
    public int Id { get; set; }
    public string Name { get; set; }

    public List<Attendant> Attendants { get; set; } = new List<Attendant>();
    public List<Request> Requests { get; set; } = new List<Request>();
}