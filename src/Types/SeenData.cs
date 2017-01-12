using System.Collections.Generic;

namespace UB3RB0T
{
    public class SeenData
    {
        public List<SeenUserData> Users { get; set; }
    }

    public class SeenUserData
    {
        public string Name { get; set; }
        public string Server { get; set; }
        public string Channel { get; set; }
        public string Text { get; set; }
        public long Timestamp { get; set; }
    }
}
