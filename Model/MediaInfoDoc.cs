
using Newtonsoft.Json;

namespace MediaGalleryConsole.Model
{
    public class MediaInfoDoc
    {
        public string? id { get; set; }                      // make it a GUID (not Name)
        public int MediaTypeId { get; set; }                // partitionKey  /MediaTypeId
        public string? Name { get; set; }                    // name of the file     Add a unique key for /MediaTypeId,/Name             
        public DateTime MediaDateTime { get; set; }         // "1932-01-01T00:00:00"
        public long MediaDateTimeVal { get; set; }          // 1932010100 - Integer value of the DateTime down to the hour
        public string? CategoryTags { get; set; }
        public string? MenuTags { get; set; }
        public string? AlbumTags { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? People { get; set; }              
        public bool ToBeProcessed { get; set; }
        public string? SearchStr { get; set; }               // name, title, people, description in lowercase

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
