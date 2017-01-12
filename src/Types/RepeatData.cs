namespace UB3RB0T
{
    using System.Collections.Generic;

    public class RepeatData
    {
        public HashSet<string> Nicks { get; set; } = new HashSet<string>();
        public string Text { get; set; }

        public void Reset(string nick, string text)
        {
            this.Nicks.Clear();
            if (!string.IsNullOrEmpty(nick))
            {
                this.Nicks.Add(nick);
            }
            this.Text = text;
        }
    }
}
