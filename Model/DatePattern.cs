using System.Text.RegularExpressions;

namespace MediaGalleryConsole.Model
{
    public class DatePattern
    {
        public Regex regex;
        public string dateParseFormat;
        public DatePattern(Regex regex, string dateParseFormat)
        {
            this.regex = regex;
            this.dateParseFormat = dateParseFormat;
        }   
    }
}
