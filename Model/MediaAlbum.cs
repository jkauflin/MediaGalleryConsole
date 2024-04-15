using Newtonsoft.Json;

namespace MediaGalleryConsole.Model
{
    public class MediaAlbum
    {
        public string id { get; set; }                      // String of MediaAlbumId
        public int MediaAlbumId { get; set; }               // partitionKey
        public string AlbumKey { get; set; }                    // name of the Album
        public string AlbumName { get; set; }                    // name of the Album
        public string AlbumDesc { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
