using Akka.Actor;
using System.Collections.Generic;

namespace MovieTicketingApp
{
    public class TicketStateMachine
    {
        public int totalTicketCount { get; }

        //Main state
        //The number of tickets available for purchase, including the reserved ticket
        public int availableTicketCount { get; private set; }
        public ReservationLog reservationLog { get; }

        //Tentative state
        public int tentativeAvailableTicketCount { get; private set; }
        public ReservationLog tentativeReservationLog { get; private set; }

        //These objects are used to persist data to disk
        private string nodeName;
        private IActorRef storageActor;

        public TicketStateMachine(int totalTicketCount, string nodeName, IActorRef storageActor)
        {
            this.totalTicketCount = totalTicketCount;

            //Main state
            this.availableTicketCount = totalTicketCount;
            this.reservationLog = new ReservationLog();

            //Tentative state
            this.tentativeAvailableTicketCount = totalTicketCount;
            this.tentativeReservationLog = new ReservationLog();

            this.nodeName = nodeName;
            this.storageActor = storageActor;
            // this.reservedAndSoldTickets = 
        }
        public void reserveTicket(string frontEndNodePath, int quantity, bool isTentative)
        {
            //If the quantity is negative, it means a node just gives up its reservation
            if (isTentative)
            {
                this.tentativeReservationLog.updateReservedCount(frontEndNodePath, this.tentativeReservationLog.getReservedCount(frontEndNodePath) + quantity);
            }
            else
            {
                this.reservationLog.updateReservedCount(frontEndNodePath, this.reservationLog.getReservedCount(frontEndNodePath) + quantity);
            }
        }
        public void sellTicket(string frontEndNodePath, int quantity, bool isTentative)
        {
            //Decrement the available ticket count
            if (isTentative)
            {
                this.tentativeAvailableTicketCount -= quantity;
            }
            else
            {
                this.availableTicketCount -= quantity;
            }
            //Decrement the reserved ticket count appropriately
            this.reserveTicket(frontEndNodePath, -quantity, isTentative);
        }
        public void recomputeTentativeState(List<LogEntry> uncommitedEntries)
        {
            this.tentativeAvailableTicketCount = this.availableTicketCount;
            this.tentativeReservationLog = this.reservationLog.clone();
            foreach (LogEntry entry in uncommitedEntries)
            {
                switch (entry.command)
                {
                    case ENTRY_COMMAND.RESERVE:
                        this.reserveTicket(entry.clientActorPath, entry.quantity, true);
                        break;
                    case ENTRY_COMMAND.SELL:
                        this.sellTicket(entry.clientActorPath, entry.quantity, true);
                        break;
                    case ENTRY_COMMAND.GIVE_UP_RESERVATION:
                        this.reserveTicket(entry.clientActorPath, -entry.quantity, true);
                        break;
                }
            }
        }
        public string getString()
        {
            return string.Concat("Available tickets: ", this.availableTicketCount, " - Reserved Tickets: ", this.reservationLog.totalReserved, this.reservationLog.getString());
        }
    }

    public class ReservationLog
    {
        public Dictionary<string, int> log { get; }
        public int totalReserved { get; private set; }
        public ReservationLog()
        {
            this.log = new Dictionary<string, int>();
        }
        public int getReservedCount(string frontEndPath)
        {
            int ticketCount = 0;
            this.log.TryGetValue(frontEndPath, out ticketCount);
            return ticketCount;
        }
        public void updateReservedCount(string frontEndPath, int quantity)
        {
            if (this.log.ContainsKey(frontEndPath)) { this.log.Remove(frontEndPath); }
            this.log.Add(frontEndPath, quantity);
            this.updateTotalReserved();
        }
        public void updateTotalReserved()
        {
            this.totalReserved = 0;
            foreach (KeyValuePair<string, int> entry in this.log)
            {
                this.totalReserved += entry.Value;
            }
        }
        public string getString()
        {
            string result = string.Empty;
            foreach (KeyValuePair<string, int> entry in this.log)
            {
                result = string.Concat(result, " - ", entry.Key.Substring(entry.Key.LastIndexOf('/') + 1), " reserved ", entry.Value);
            }
            return result;
        }
        public ReservationLog clone()
        {
            ReservationLog result = new ReservationLog();
            foreach (KeyValuePair<string, int> entry in this.log)
            {
                result.updateReservedCount(entry.Key, entry.Value);
            }
            return result;
        }
    }

}