using System;
using System.Collections;
using Akka.Actor;
using Akka.Event;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MovieTicketingApp
{
    public class Log
    {
        public List<LogEntry> log { get; }
        public string tentativeState;

        public Log()
        {
            log = new List<LogEntry>();
        }

        public Log(List<LogEntry> entries)
        {
            this.log = entries;
            this.tentativeState = string.Empty;
            //Compute the tentative state from the default entries
            this.computeTentativeState();
        }

        public void append(int term, int ticketQuantity, ENTRY_COMMAND command, string clientActorPath, bool responseSent, string giveUpForFrontEndPath)
        {
            this.log.Add(new LogEntry(term, getLastLogIndex() + 1, ticketQuantity, command, clientActorPath, responseSent, giveUpForFrontEndPath));
            this.computeTentativeState();
        }

        public int getLastLogIndex()
        {
            if (log.Count == 0) { return 0; }
            return this.log[log.Count - 1].index;
        }

        public int getLastLogTerm()
        {
            int lastLogTerm = 0;
            if (log.Count > 0) { lastLogTerm = log[log.Count - 1].term; }
            return lastLogTerm;
        }

        public int getLogEntryTerm(int index)
        {
            int term = 0;
            if (log.Count > 0 && index > 0 && log.Count >= index) { term = log[index - 1].term; }
            return term;
        }

        public LogEntry getLogEntry(int index)
        {
            LogEntry result = null;
            if (log.Count >= index) { result = log[index - 1]; }
            return result;
        }

        // public string getEntriesString()
        // {
        //     string strEntries = "";
        //     foreach (LogEntry entry in this.log)
        //     {
        //         strEntries = string.Concat(strEntries, "(Ind: ", entry.index, " - Val: ", entry.value, " - T: ", entry.term, ") ");
        //     }
        //     return strEntries;
        // }

        public List<LogEntry> getEntriesFromIndex(int fromEntryIndex)
        {
            List<LogEntry> results = new List<LogEntry>();
            if (fromEntryIndex < 1) { fromEntryIndex = 1; }
            if (fromEntryIndex > this.log.Count) { return results; }
            for (int i = fromEntryIndex - 1; i < this.log.Count; i++)
            {
                results.Add(this.log[i]);
            }
            return results;
        }

        public void removeEntriesFromIndex(int startIndex)
        {
            if (startIndex < 1) { startIndex = 1; }
            for (int i = this.log.Count - 1; i >= startIndex - 1; i--)
            {
                this.log.RemoveAt(i);
            }
        }
        public void computeTentativeState()
        {
            //Recompute the tentative state of the state machine
            // this.tentativeState = string.Empty;
            // foreach (LogEntry entry in this.log)
            // {
            //     if (!string.IsNullOrEmpty(this.tentativeState)) { this.tentativeState = string.Concat(this.tentativeState, " - "); }
            //     this.tentativeState = string.Concat(this.tentativeState, entry.value);
            // }
        }
        public string getTentativeState()
        {
            return tentativeState;
        }
    }

    public enum ENTRY_COMMAND
    {
        RESERVE = 1,
        SELL = 2,
        GIVE_UP_RESERVATION = 3
    }

    public class LogEntry
    {
        public int term { get; }
        public int index { get; }
        public string clientActorPath { get; }
        public bool responseSent { get; set; }
        //If isSold is false, the ticket is reserved for the front end
        public ENTRY_COMMAND command { get; }
        public int quantity { get; }
        public string giveUpForFrontEndPath { get; }
        public LogEntry(int term, int index, int quantity, ENTRY_COMMAND command, string clientActorPath, bool responseSent, string giveUpForFrontEndPath)
        {
            this.term = term;
            this.index = index;
            this.quantity = quantity;
            this.command = command;
            this.clientActorPath = clientActorPath;
            this.responseSent = responseSent;
            this.giveUpForFrontEndPath = giveUpForFrontEndPath;
        }
    }
}