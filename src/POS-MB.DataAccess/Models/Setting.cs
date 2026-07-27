namespace POS_MB.DataAccess.Models;

public class Setting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}
