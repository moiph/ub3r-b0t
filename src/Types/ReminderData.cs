namespace UB3RB0T
{
    public class ReminderData
    {
        public string Reason { get; set; }
        public string Nick { get; set; }
        public string UserId { get; set; }
        public string Id { get; set; }
        public string Duration { get; set; }
        public string Time { get; set; }
        public string Channel { get; set; }
        public string Requestor { get; set; }
        public string Recurring { get; set; }
        public string Server { get; set; }
        public BotType BotType { get; set; }

        public string RequestedBy
        {
            get
            {
                return string.IsNullOrEmpty(this.Requestor) ? string.Empty : "[Requested by " + this.Requestor + "]";
            }
        }
    }
}

