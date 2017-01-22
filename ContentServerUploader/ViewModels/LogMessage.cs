namespace Bulky.ViewModels
{
    public class LogMessage
    {
        public LogMessage(string msg, Severity severity)
        {
            Message = msg;
            Severity = severity;
        }

        public LogMessage(string msg)
        {
            Message = msg;
            Severity = Severity.Info;
        }
        public string Message { get; set; }
        public Severity Severity { get; set; }

        public override string ToString()
        {
            return Message;
        }
    }
}