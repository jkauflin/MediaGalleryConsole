
namespace MediaGalleryConsole.Model
{
    public class MetricPoint
    {
        public string id { get; set; }                      // GUID
        public int PointDay { get; set; }                   // partitionKey (timestamp day value yyyyMMdd)
        public DateTime PointDateTime { get; set; }
        public long PointYearMonth { get; set; }            // int.Parse(takenDT.ToString("yyyyMM"))
        public long PointDayTime { get; set; }              // int.Parse(takenDT.ToString("yyHHmmss"))
        public string pvVolts { get; set; }
        public string pvAmps { get; set; }
        public string pvWatts { get; set; }
    }
}
