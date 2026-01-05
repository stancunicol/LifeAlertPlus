namespace LifeAlertPlus.Domain.Entities
{
    public class Monitored
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime? Birthdate { get; set; }
        public string Gender { get; set; }
        public string Address { get; set; }
        public long? UpdateFrequency { get; set; }
        public string DeviceSerialNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
