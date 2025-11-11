namespace LifeAlertPlus.Domain.Entities
{
    public class UserMonitored
    {
        public Guid IdUser { get; set; }
        public User User { get; set; }
        public Guid IdMonitored { get; set; }
        public Monitored Monitored { get; set; }
        public string Relationship { get; set; }
    }
}