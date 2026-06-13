namespace CarsWebsite;

public class UserNotificationSetting
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Category { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; } = true;
}
