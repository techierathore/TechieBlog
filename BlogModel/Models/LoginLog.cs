namespace BlogModels
{
    public class LoginLog
    {

        public long LoginLogId { get; set; }
        public long LoginUserId { get; set; }
        public DateTime LoginDateTime { get; set; }
        public string ClientIP { get; set; }
        public DateTime LogOutDateTime { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName
        {
            get
            {
                return FirstName + " " + LastName;
            }
        }
    }

    public class ReportInput
    {
        public long AppUserId { get; set; }
        public long JatakUserId { get; set; }
        public byte Bhav { get; set; }
        public string Planet { get; set; }
        public string RuleType { get; set; }
        public string NatureType { get; set; }

    }
}
