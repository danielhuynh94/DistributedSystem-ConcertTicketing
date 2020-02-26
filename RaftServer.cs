using System;
using Akka.Actor;
using Akka.Event;
using System.Collections.Generic;

namespace MovieTicketingApp
{

    public class RaftServer : UntypedActor
    {
        public ILoggingAdapter log = Context.GetLogger();
        protected string lastMessage = null;

        public int nodeId { get; }

        //States
        public IRaftNodeState followerState { get; }
        public IRaftNodeState candidateState { get; }
        public IRaftNodeState leaderState { get; }
        protected IRaftNodeState currentState = null;
        ////////////////////////////////////////////


        //Persistent states
        public int currentTerm { get; set; }
        public int votedForCandidateId { get; set; }
        public Log logEntries { get; private set; }
        ///////////////////////////////////////////

        public int commitIndex { get; private set; }
        public void setCommitIndex(int index)
        {
            this.commitIndex = Math.Min(index, this.logEntries.getLastLogIndex());
        }

        //Simple State Machine - commands in the log entries will be applied to this state machine
        public TicketStateMachine stateMachine;
        ////////////////////////////////////////////

        public int lastApplied { get; private set; }
        public void updateLastApplied()
        {
            if (commitIndex > lastApplied)
            {
                this.lastApplied++;
                this.applyLogAtLastAppliedToStateMachine();
            }
        }
        public void applyLogAtLastAppliedToStateMachine()
        {
            //Apply the log
            LogEntry entry = this.logEntries.getLogEntry(this.lastApplied);
            switch (entry.command)
            {
                case ENTRY_COMMAND.RESERVE:
                    this.stateMachine.reserveTicket(entry.clientActorPath, entry.quantity, false);
                    break;
                case ENTRY_COMMAND.SELL:
                    this.stateMachine.sellTicket(entry.clientActorPath, entry.quantity, false);
                    break;
                case ENTRY_COMMAND.GIVE_UP_RESERVATION:
                    this.stateMachine.reserveTicket(entry.clientActorPath, -entry.quantity, false);
                    break;
            }

            //If this is not the leader, don't worry about responding to client
            //If this is a load from persistent data, and the response was sent, return
            //If there is no client path to respond, return
            if (!this.currentState.Equals(this.leaderState) || entry.responseSent || string.IsNullOrEmpty(entry.clientActorPath)) { return; }

            //Respond to the client
            ActorSelection clientSelection = actorSystem.ActorSelection(entry.clientActorPath);
            if (clientSelection != null)
            {
                switch (entry.command)
                {
                    case ENTRY_COMMAND.RESERVE:
                        clientSelection.Tell(new ReserveTicketResponse(true, entry.quantity, 0, null));
                        break;
                    case ENTRY_COMMAND.SELL:
                        clientSelection.Tell(new PurchaseTicketResponse(true, entry.quantity, null));
                        break;
                    case ENTRY_COMMAND.GIVE_UP_RESERVATION:
                        clientSelection.Tell(new GiveUpReservedTicketResponse(true, entry.quantity, entry.giveUpForFrontEndPath, null));
                        break;
                }
            }
            entry.responseSent = true;
            this.updatePersistentStorage();
        }

        public IActorRef leaderRef { get; private set; }

        //List containg all nodes including this node
        public List<IActorRef> allNodes { get; }

        //Actor system used to get the client to respond later
        public ActorSystem actorSystem { get; }

        //Heartbeat timeout interval
        public int heartBeatTimeoutInterval = 50;

        //Followers' next indexes -- Only Leaders use
        public IndexDict followerNextIndexes;

        //Followers' match indexes -- Only Leaders use
        public IndexDict followerMatchIndexes;

        //Send a message to this actor whenever we need to write data to disk
        protected IActorRef storageActor;

        protected Random randomNumberGenerator;

        const int TOTAL_AVAILABLE_TICKETS = 50;

        public RaftServer(int nodeId, List<IActorRef> allNodes, IActorRef storageActor, ActorSystem actorSystem)
        {
            this.nodeId = nodeId;
            this.logEntries = new Log();
            this.followerState = new FollowerState(this);
            this.candidateState = new CandidateState(this);
            this.leaderState = new LeaderState(this);
            this.stateMachine = new TicketStateMachine(TOTAL_AVAILABLE_TICKETS, Self.Path.Name, storageActor);
            this.allNodes = allNodes;
            this.storageActor = storageActor;
            this.currentState = this.followerState;
            this.actorSystem = actorSystem;
            this.randomNumberGenerator = new Random();
            this.resetElectionTimer();
        }

        protected override void PreStart()
        {
            this.loadDataFromDisk();
        }

        protected override void PreRestart(Exception reason, object message)
        {
            this.loadDataFromDisk();
            //this.log.Info(string.Concat("Node ", Self.Path.Name, " PreRestart"));
        }

        protected void loadDataFromDisk()
        {
            // If we have persistent data on disk, get it
            if (FileManagement.checkFileExists(Self.Path.Name))
            {
                Storage storage = FileManagement.readFile(Self.Path.Name);
                if (storage != null)
                {
                    this.currentTerm = storage.currentTerm;
                    this.votedForCandidateId = storage.votedFor;
                    this.logEntries = new Log(storage.entries);
                    this.commitIndex = storage.commitIndex;
                    while (this.commitIndex > this.lastApplied)
                    {
                        this.updateLastApplied();
                    }
                }
            }
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ReceiveTimeout rt:
                    this.currentState.handleReceiveTimeout();
                    break;
                case RequestVote requestVote:
                    this.checkTerm(requestVote.term);
                    this.currentState.handleRequestVote(requestVote, Sender);
                    break;
                case RequestVoteResponse requestVoteResponse:
                    this.checkTerm(requestVoteResponse.term);
                    this.currentState.handleRequestVoteResponse(requestVoteResponse);
                    break;
                case AppendEntries appendEntries:
                    this.checkTerm(appendEntries.term);
                    this.currentState.handleAppendEntries(appendEntries, Sender);
                    break;
                case AppendEntriesResponse appendEntriesResponse:
                    this.checkTerm(appendEntriesResponse.term);
                    this.currentState.handleAppendEntriesResponse(appendEntriesResponse, Sender);
                    break;
                case End e:
                    this.log.Info(string.Concat("Node ", this.nodeId, " is shutting down."));
                    Context.Stop(Self);
                    break;
                case KillLeader k:
                    if (this.currentState.Equals(this.leaderState))
                    {
                        Self.Tell(Kill.Instance);
                    }
                    break;
                case PoisonLeader p:
                    if (this.currentState.Equals(this.leaderState))
                    {
                        Self.Tell(PoisonPill.Instance);
                    }
                    break;
                //The following events are for testing
                case GetLeaderPath g:
                    Sender.Tell(this.leaderRef.Path.ToString());
                    break;
                case PrintStateMachine p:
                    log.Info(this.stateMachine.getString());
                    break;
                //Movie Ticketing
                case ReserveTicket reserveTicket:
                    this.currentState.handleReserveTicket(reserveTicket, Sender);
                    break;
                case PurchaseTicket purchaseTicket:
                    this.currentState.handlePurchaseTicket(purchaseTicket, Sender);
                    break;
                case QueryTicket queryTicket:
                    this.handleQueryTicket();
                    break;
                case GiveUpReservedTicket request:
                    this.currentState.handleGiveUpReservedTicket(request, Sender);
                    break;
                case Ping ping:
                    //Just reply with a ping response
                    Sender.Tell(new PingResponse());
                    break;
            }
        }

        public void updatePersistentStorage()
        {
            this.storageActor.Tell(new Storage(Self.Path.Name, this.currentTerm, this.votedForCandidateId, this.logEntries.log, this.commitIndex));
        }

        protected void checkTerm(int messageTerm)
        {
            //If the message term is greater than the node's term
            //Adjust the node's term and move back to Follower state
            if (this.currentTerm >= messageTerm) { return; }
            this.currentTerm = messageTerm;
            if (!this.currentState.Equals(this.followerState))
            {
                this.setState(this.followerState);
            }
            else
            {
                this.currentState.init();
            }
            this.updatePersistentStorage();
        }

        public void setState(IRaftNodeState targetState)
        {
            //Change to the target state
            this.currentState = targetState;
            //Restart the target state
            this.currentState.init();
            if (targetState.Equals(this.leaderState)) { this.leaderRef = Self; }
        }

        public void resetElectionTimer()
        {
            //Election timeout should be between 150 to 300 milseconds
            int electionTimeoutInterval = this.randomNumberGenerator.Next(150, 301);
            Context.SetReceiveTimeout(TimeSpan.FromMilliseconds(electionTimeoutInterval));
        }

        //This is an Inconsistent Read
        public void handleQueryTicket()
        {
            int lastLogIndex = this.logEntries.getLastLogIndex();
            if (lastLogIndex == lastApplied)
            {
                //Return the committed state
                Context.Sender.Tell(new QueryTicketResponse(this.stateMachine.availableTicketCount, this.stateMachine.reservationLog));
            }
            else
            {
                //Return the tentative state
                Context.Sender.Tell(new QueryTicketResponse(this.stateMachine.tentativeAvailableTicketCount, this.stateMachine.tentativeReservationLog));
            }
        }

        #region  "Follower functions"
        protected bool checkVote(RequestVote voteRequest)
        {
            //Reply false if term < currentTerm
            if (voteRequest.term < this.currentTerm) { return false; }
            //Reply false if already voted
            if (this.votedForCandidateId > 0) { return false; }
            //Reply false if the term of this node's last log > the term of requester's last log
            if (this.logEntries.getLastLogTerm() > voteRequest.lastLogTerm) { return false; }
            //Reply false if the terms equal, but the requester's last index is smaller
            if (this.logEntries.getLastLogTerm() == voteRequest.lastLogTerm
                && this.logEntries.getLastLogIndex() > voteRequest.lastLogIndex) { return false; }
            return true;
        }

        public void respondVoteRequest(RequestVote voteRequest, IActorRef sender)
        {
            //Check whether this node can vote for the requester
            bool vote = this.checkVote(voteRequest);

            //If we can vote for this candidate, vote and record it
            if (vote)
            {
                this.votedForCandidateId = voteRequest.candidateID;
                this.resetElectionTimer();
            }

            this.updatePersistentStorage();

            //Reply to the candidate
            sender.Tell(new RequestVoteResponse(this.currentTerm, vote));
        }

        public void setLeaderReference(IActorRef leaderRef)
        {
            this.leaderRef = leaderRef;
        }

        public void notifyClientOfLeaderAddress(object message, IActorRef sender)
        {
            switch (message)
            {
                case ReserveTicket reserveTicket:
                    sender.Tell(new ReserveTicketResponse(false, 0, reserveTicket.requestQuantity, this.leaderRef));
                    break;
                case PurchaseTicket purchaseTicket:
                    sender.Tell(new PurchaseTicketResponse(false, purchaseTicket.purchaseQuantity, this.leaderRef));
                    break;
                case GiveUpReservedTicket giveUpRequest:
                    sender.Tell(new GiveUpReservedTicketResponse(false, giveUpRequest.quantity, giveUpRequest.giveUpForFrontEndPath, this.leaderRef));
                    break;
            }
            this.resetElectionTimer();
        }

        public void appendEntriesFromLeader(AppendEntries appendEntries, IActorRef leader)
        {
            //Reply false if term < currentTerm
            //Reply false if log does not contain an entry at prevLogIndex whose term matches prevLogTerm
            if (appendEntries.term < this.currentTerm
                || this.logEntries.getLastLogIndex() < appendEntries.prevLogIndex
                || (appendEntries.prevLogIndex > 0 && this.logEntries.getLogEntryTerm(appendEntries.prevLogIndex) != appendEntries.prevLogTerm))
            {
                leader.Tell(new AppendEntriesResponse(this.currentTerm, false, 0));
                return;
            }

            //If an existing entry conflicts with a new one (same index but different terms), delete the existing entry and all that follow it
            int startingIndexToRemove = this.logEntries.getLastLogIndex() + 1;
            foreach (LogEntry entry in appendEntries.entries)
            {
                if (this.logEntries.getLogEntryTerm(entry.index) != entry.term)
                {
                    startingIndexToRemove = entry.index;
                    break;
                }
            }

            //Remove
            if (startingIndexToRemove <= this.logEntries.getLastLogIndex())
            {
                this.logEntries.removeEntriesFromIndex(startingIndexToRemove);
            }

            //Append the missing entries
            foreach (LogEntry entry in appendEntries.entries)
            {
                if (entry.index <= this.logEntries.getLastLogIndex()) { continue; }
                //For followers, default responseSent to true, it's the leader's job
                this.logEntries.append(entry.term, entry.quantity, entry.command, entry.clientActorPath, true, entry.giveUpForFrontEndPath);
            }

            this.updatePersistentStorage();

            //Tell the leader it has been successful
            leader.Tell(new AppendEntriesResponse(this.currentTerm, true, this.logEntries.getLastLogIndex()));

            //Update the commit index
            if (this.commitIndex < appendEntries.commitIndex)
            {
                this.setCommitIndex(appendEntries.commitIndex);
            }

            this.updateLastApplied();

            //Recompute the tentative state
            this.stateMachine.recomputeTentativeState(this.logEntries.getEntriesFromIndex(this.lastApplied + 1));

        }


        #endregion

        #region  "Candidate functions"
        public void sendVoteRequest()
        {
            foreach (IActorRef node in this.allNodes)
            {
                if (node.Equals(Self)) { continue; }
                node.Tell(new RequestVote(this.nodeId, this.currentTerm, this.logEntries.getLastLogIndex(), this.logEntries.getLastLogTerm()));
            }
        }

        #endregion

        #region "Leader functions"
        public void initFollowerNextIndexes()
        {
            this.followerNextIndexes = new IndexDict();
            foreach (IActorRef node in allNodes)
            {
                if (node.Equals(Self)) { continue; }
                this.followerNextIndexes.updateValue(node, this.logEntries.getLastLogIndex() + 1);
            }
        }

        public void initFollowerMatchIndexes()
        {
            this.followerMatchIndexes = new IndexDict();
            foreach (IActorRef node in allNodes)
            {
                if (node.Equals(Self)) { continue; }
                this.followerMatchIndexes.updateValue(node, 0);
            }
        }

        public void sendAppendEntries()
        {

            foreach (IActorRef node in this.allNodes)
            {
                if (node.Equals(Self)) { continue; }
                this.sendAppendEntryToNode(node);
            }

            //Reset the heartbeat timer
            this.resetHeartBeatTimer();
        }

        protected void sendAppendEntryToNode(IActorRef targetNode)
        {
            int nextLogIndex = this.followerNextIndexes.getValue(targetNode);
            int prevLogIndex = nextLogIndex - 1;
            int prevLogTerm = this.logEntries.getLogEntryTerm(prevLogIndex);
            List<LogEntry> entries = this.logEntries.getEntriesFromIndex(nextLogIndex);
            targetNode.Tell(new AppendEntries(this.currentTerm, this.nodeId, prevLogIndex, prevLogTerm, entries, this.commitIndex), Self);
        }

        public void processAppendEntriesResponse(AppendEntriesResponse appendEntriesResponse, IActorRef sender)
        {
            //If false, reduce the next index, but make sure the next index does not fall below 1, return
            if (!appendEntriesResponse.success)
            {
                this.followerNextIndexes.updateValue(sender, Math.Max(this.followerNextIndexes.getValue(sender) - 1, 1));
                //When leader sees failed AppendEntries RPC, immediately send new RPC
                this.sendAppendEntryToNode(sender);
                return;
            }

            //Got a success response from a follower, update its next index
            this.followerNextIndexes.updateValue(sender, appendEntriesResponse.lastLogIndex + 1);
            this.followerMatchIndexes.updateValue(sender, appendEntriesResponse.lastLogIndex);

            //Update commit index
            int newCommitIndex = 0;

            //For each index after the last commit index, check which one has the most match
            for (int i = this.commitIndex + 1; i < this.logEntries.getLastLogIndex() + 1; i++)
            {
                //Init to 1 to include self
                int matchCount = 1;

                foreach (KeyValuePair<IActorRef, int> entry in followerMatchIndexes.entries) { if (entry.Value >= i) { matchCount++; } }

                //If the match count is of majority of the nodes and the current term
                if (matchCount > (this.allNodes.Count / 2) && this.currentTerm == this.logEntries.getLogEntryTerm(i))
                {
                    newCommitIndex = i;
                }
            }

            if (newCommitIndex > this.commitIndex)
            {
                this.setCommitIndex(newCommitIndex);
            }

            this.updateLastApplied();

        }

        public void processReserveTicketRequest(ReserveTicket reserveTicket, IActorRef sender)
        {
            //TODO:
            //If the reserve request > the available for reservation, reply with false
            if (reserveTicket.requestQuantity > (this.stateMachine.availableTicketCount - this.stateMachine.reservationLog.totalReserved))
            {
                sender.Tell(new ReserveTicketResponse(false, 0, reserveTicket.requestQuantity, null));
                return;
            }

            this.logEntries.append(this.currentTerm, reserveTicket.requestQuantity, ENTRY_COMMAND.RESERVE, sender.Path.ToString(), false, string.Empty);
            this.updatePersistentStorage();

            //Update the tentative state
            this.stateMachine.reserveTicket(sender.Path.ToString(), reserveTicket.requestQuantity, true);

            this.log.Info(string.Concat(Self.Path.Name, " received reservation request of ", reserveTicket.requestQuantity, " tickets from ", sender.Path.Name));
            // int successfullyReservedQuantity = Math.Min(this.stateMachine.availableTicketCount - this.stateMachine.reservedTicketCount, reserveTicket.requestQuantity);
            // sender.Tell(new ReserveTicketResponse(true, successfullyReservedQuantity, reserveTicket.requestQuantity, null));
        }

        public void processPurchaseTicketRequest(PurchaseTicket purchaseTicket, IActorRef sender)
        {
            //Make sure the purchase quantity does not exceed the quantity reserved for this front-end, this case should never happen

            //Add a log entry for the purchase
            this.logEntries.append(this.currentTerm, purchaseTicket.purchaseQuantity, ENTRY_COMMAND.SELL, sender.Path.ToString(), false, string.Empty);
            this.updatePersistentStorage();

            //Update the tentative state
            this.stateMachine.sellTicket(sender.Path.ToString(), purchaseTicket.purchaseQuantity, true);

            this.log.Info(string.Concat(Self.Path.Name, " received purchase request of ", purchaseTicket.purchaseQuantity, " tickets from ", sender.Path.Name));
        }

        public void processGiveUpReservedTicketRequest(GiveUpReservedTicket giveUpReservedTicket, IActorRef sender)
        {
            //Add a log entry for the give-up
            this.logEntries.append(this.currentTerm, giveUpReservedTicket.quantity, ENTRY_COMMAND.GIVE_UP_RESERVATION, sender.Path.ToString(), false, giveUpReservedTicket.giveUpForFrontEndPath);
            this.updatePersistentStorage();

            //Update the tentative state
            this.stateMachine.reserveTicket(sender.Path.ToString(), -giveUpReservedTicket.quantity, true);

            this.log.Info(string.Concat(Self.Path.Name, " received give-up reservation request of ", giveUpReservedTicket.quantity, " tickets from ", sender.Path.Name));
        }

        public void resetHeartBeatTimer()
        {
            Context.SetReceiveTimeout(TimeSpan.FromMilliseconds(this.heartBeatTimeoutInterval));
        }

        #endregion
    }

}