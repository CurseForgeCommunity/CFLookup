using System.Data;

namespace CFLookup.Models
{
    public class FileProcessingStatus
    {
        public Guid FileProcessingStatusId {  get; set; }
        public DateTimeOffset Created_UTC {  get; set; }
        public DateTimeOffset Last_Updated_UTC {  get; set; }
        public int GameId {  get; set; }
        public int ModId {  get; set; }
        public int FileId {  get; set; }

        public FileProcessingStatus(DataRow row)
        {
            FileProcessingStatusId = (Guid)row["fileProcessingStatusId"];
            Created_UTC = (DateTimeOffset)row["created_utc"];
            Last_Updated_UTC = (DateTimeOffset)row["last_updated_utc"];
            GameId = (int)row["gameId"];
            ModId = (int)row["modId"];
            FileId = (int)row["fileId"];
        }
    }
}
