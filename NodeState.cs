using System;
using Akka.Actor;
using Akka.Event;

namespace MovieTicketingApp
{

    public interface IRaftNodeState
    {
        void init();
        void handleRequestVote(RequestVote requestVote, IActorRef sender);
        void handleAppendEntries(AppendEntries appendEntries, IActorRef sender);
        void handleReceiveTimeout();
        void handleRequestVoteResponse(RequestVoteResponse requestVoteResponse);
        void handleAppendEntriesResponse(AppendEntriesResponse appendEntriesResponse, IActorRef sender);
        void handleReserveTicket(ReserveTicket reserveTicket, IActorRef sender);
        void handlePurchaseTicket(PurchaseTicket purchaseTicket, IActorRef sender);
        void handleGiveUpReservedTicket(GiveUpReservedTicket giveUpReservedTicket, IActorRef sender);
    }

    public class FollowerState : IRaftNodeState
    {
        protected RaftServer node;

        public FollowerState(RaftServer node)
        {
            this.node = node;
        }

        public void init()
        {
            this.node.votedForCandidateId = 0;
            this.node.resetElectionTimer();

            this.node.log.Info(string.Concat("Node ", this.node.nodeId, " is in Follower state"));
        }

        public void handleReceiveTimeout()
        {
            this.node.setState(this.node.candidateState);
        }

        public void handleRequestVote(RequestVote voteRequest, IActorRef sender)
        {
            this.node.respondVoteRequest(voteRequest, sender);
        }

        public void handleAppendEntries(AppendEntries appendEntries, IActorRef sender)
        {
            //Save the info of the leader
            this.node.setLeaderReference(sender);
            //Process the entries from the leader
            this.node.appendEntriesFromLeader(appendEntries, sender);
            //this.node.log.Info(string.Concat("Follower ", this.node.nodeId, " received Append Entries message."));
            this.node.resetElectionTimer();
        }

        public void handleRequestVoteResponse(RequestVoteResponse requestVoteResponse)
        {
            //Do nothing
        }

        public void handleAppendEntriesResponse(AppendEntriesResponse appendEntriesResponse, IActorRef sender)
        {
            //Do nothing
        }

        public void handleReserveTicket(ReserveTicket reserveTicket, IActorRef sender)
        {
            this.node.notifyClientOfLeaderAddress(reserveTicket, sender);
        }

        public void handlePurchaseTicket(PurchaseTicket purchaseTicket, IActorRef sender)
        {
            this.node.notifyClientOfLeaderAddress(purchaseTicket, sender);
        }

        public void handleGiveUpReservedTicket(GiveUpReservedTicket giveUpReservedTicket, IActorRef sender)
        {
            this.node.notifyClientOfLeaderAddress(giveUpReservedTicket, sender);
        }

    }

    public class CandidateState : IRaftNodeState
    {
        public RaftServer node;

        protected int receivedVoteCount = 0;

        public CandidateState(RaftServer node)
        {
            this.node = node;
        }

        public void init()
        {
            //Increment the term
            this.node.currentTerm += 1;
            //Vote for itself
            this.node.votedForCandidateId = this.node.nodeId;
            this.receivedVoteCount = 1;
            this.node.updatePersistentStorage();
            //Reset the election timer
            this.node.resetElectionTimer();
            //Request votes from other nodes
            this.node.sendVoteRequest();

            this.node.log.Info(string.Concat("Node ", this.node.nodeId, " is in Candidate state"));
        }

        public void handleReceiveTimeout()
        {
            //If times out, go back to follower state
            this.node.setState(this.node.followerState);
        }

        public void handleRequestVote(RequestVote requestVote, IActorRef sender)
        {
            //If it gets here, the sender's term is <= this node's term, vote no
            sender.Tell(new RequestVoteResponse(this.node.currentTerm, false));
        }

        public void handleAppendEntries(AppendEntries appendEntries, IActorRef sender)
        {
            //If we get an append entries message while being a candidate
            //Go back to become a follower
            this.node.setState(this.node.followerState);
            this.node.followerState.handleAppendEntries(appendEntries, sender);
        }

        public void handleRequestVoteResponse(RequestVoteResponse requestVoteResponse)
        {
            //If we get a vote, increment the counter
            if (requestVoteResponse.voteGranted) { this.receivedVoteCount += 1; }
            //If we get more than half the votes, become a leader
            if (this.receivedVoteCount > this.node.allNodes.Count / 2) { this.node.setState(this.node.leaderState); }
        }

        public void handleAppendEntriesResponse(AppendEntriesResponse appendEntriesResponse, IActorRef sender)
        {
            //Do nothing
        }

        public void handleReserveTicket(ReserveTicket reserveTicket, IActorRef sender)
        {
            this.node.notifyClientOfLeaderAddress(reserveTicket, sender);
        }

        public void handlePurchaseTicket(PurchaseTicket purchaseTicket, IActorRef sender)
        {
            this.node.notifyClientOfLeaderAddress(purchaseTicket, sender);
        }

        public void handleGiveUpReservedTicket(GiveUpReservedTicket giveUpReservedTicket, IActorRef sender)
        {
            this.node.notifyClientOfLeaderAddress(giveUpReservedTicket, sender);
        }

    }

    public class LeaderState : IRaftNodeState
    {
        public RaftServer node;

        public LeaderState(RaftServer node)
        {
            this.node = node;
        }

        public void init()
        {
            this.node.initFollowerNextIndexes();
            this.node.initFollowerMatchIndexes();
            this.node.sendAppendEntries();
            this.node.log.Info(string.Concat("Node ", this.node.nodeId, " is in Leader state"));
        }

        public void handleReceiveTimeout()
        {
            this.node.sendAppendEntries();
        }

        public void handleRequestVote(RequestVote requestVote, IActorRef sender)
        {
            //If it gets here, the sender's term is <= this node's term, vote no
            sender.Tell(new RequestVoteResponse(this.node.currentTerm, false));
        }

        public void handleAppendEntries(AppendEntries appendEntries, IActorRef sender)
        {
            //If the appendEntries term is > this term, the checkTerm function already makes this node step down
        }

        public void handleRequestVoteResponse(RequestVoteResponse requestVoteResponse)
        {
            //Do nothing
        }

        public void handleAppendEntriesResponse(AppendEntriesResponse appendEntriesResponse, IActorRef sender)
        {
            this.node.processAppendEntriesResponse(appendEntriesResponse, sender);
        }

        public void handleReserveTicket(ReserveTicket reserveTicket, IActorRef sender)
        {
            this.node.processReserveTicketRequest(reserveTicket, sender);
        }

        public void handlePurchaseTicket(PurchaseTicket purchaseTicket, IActorRef sender)
        {
            this.node.processPurchaseTicketRequest(purchaseTicket, sender);
        }

        public void handleGiveUpReservedTicket(GiveUpReservedTicket giveUpReservedTicket, IActorRef sender)
        {
            this.node.processGiveUpReservedTicketRequest(giveUpReservedTicket, sender);
        }

    }
}