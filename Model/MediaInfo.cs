
using Newtonsoft.Json;

namespace MediaGalleryConsole.Model
{
    public class MediaInfo
    {
        public string? id { get; set; }                      // make it a GUID (not Name)
        public int MediaTypeId { get; set; }                // partitionKey  /MediaTypeId
        public string? Name { get; set; }                    // name of the file     Add a unique key for /MediaTypeId,/Name             
        public DateTime TakenDateTime { get; set; }         
        public long TakenFileTime { get; set; }         
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
