using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Akka.Actor;
using Akka.Event;

namespace MovieTicketingApp
{

    public class Storage
    {
        public string nodeName;
        public int currentTerm;
        public int votedFor;
        public List<LogEntry> entries;
        public int commitIndex;
        public Storage(string nodeName, int currentTerm, int votedFor, List<LogEntry> entries, int commitIndex)
        {
            this.nodeName = nodeName;
            this.currentTerm = currentTerm;
            this.votedFor = votedFor;
            this.entries = entries;
            this.commitIndex = commitIndex;
        }
    }

    public class FrontEndStorage
    {
        public string nodeName;
    }

    public class FileManagement
    {
        public static void writeFile(Storage storage)
        {
            string filename = string.Concat(storage.nodeName, ".json");
            // serialize JSON to a string and then write string to a file
            string output = JsonConvert.SerializeObject(filename);
            //serialize JSON directly to a file
            using (StreamWriter file = File.CreateText(filename))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, storage);
            }
        }

        public static void writeStateMachineFile(string nodeName, string state)
        {
            string filename = string.Concat(nodeName, "-state.json");
            // serialize JSON to a string and then write string to a file
            string output = JsonConvert.SerializeObject(filename);
            //serialize JSON directly to a file
            using (StreamWriter file = File.CreateText(filename))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, state);
            }
        }

        public static bool checkFileExists(string nodeName)
        {
            return File.Exists(string.Concat(nodeName, ".json"));
        }

        public static Storage readFile(string nodeName)
        {
            Storage result = null;
            string[] lines = System.IO.File.ReadAllLines(string.Concat(nodeName, ".json"));
            if (lines.Length == 0) { return result; }
            string storage = lines[0];
            result = JsonConvert.DeserializeObject<Storage>(storage);
            return result;
        }

        public static void deleteFile(string nodeName)
        {
            File.Delete(string.Concat(nodeName, ".json"));
        }
        public static void deleteStateFile(string nodeName)
        {
            File.Delete(string.Concat(nodeName, "-state.json"));
        }

    }

}