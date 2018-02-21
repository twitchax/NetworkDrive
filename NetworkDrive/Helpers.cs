using Microsoft.WindowsAzure.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetworkDrive
{
    public static class Helpers
    {
        public static string StorageConnectionString { get; set; }

        public static CloudStorageAccount Storage => CloudStorageAccount.Parse(StorageConnectionString);
    }
}
