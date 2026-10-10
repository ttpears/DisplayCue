using System;
using System.Collections.Generic;
using System.Linq;

namespace MonitorHotkeys
{
    public sealed class InputRequest { public string PhysicalId; public uint Input; }

    // Hardware-free orchestration. The same sequence is used with native and peer participants.
    public interface IHandoffParticipant
    {
        void Begin();
        IEnumerable<InputRequest> Plan();
        void Prepare();
        uint ReadInput(string identity, bool capture);
        void SetInput(string identity, uint input, bool capture);
        void Apply();
        void Commit();
        void PrepareRestore();
        IEnumerable<InputRequest> PreviousInputs();
        void RestoreLayout();
        void CompleteRestore();
    }

    public sealed class HandoffResult
    {
        public bool Committed;
        public List<string> RecoveryErrors = new List<string>();
    }

    public static class HandoffSequence
    {
        public static List<InputRequest> CombinePlans(IEnumerable<InputRequest> first, IEnumerable<InputRequest> second)
        {
            List<InputRequest> all = first.Concat(second).ToList();
            if (all.Any(x => String.IsNullOrWhiteSpace(x.PhysicalId) || x.PhysicalId == "?" || x.PhysicalId.Contains('|') || x.PhysicalId.Contains(',')))
                throw new InvalidOperationException("A monitor has no valid shared identity. Re-save its inputs or assign matching Peer IDs on both PCs.");
            foreach (var group in all.GroupBy(x => x.PhysicalId, StringComparer.OrdinalIgnoreCase))
                if (group.Select(x => x.Input).Distinct().Count() > 1) throw new InvalidOperationException("The profiles request different inputs for monitor '" + group.Key + "'.");
            return all.GroupBy(x => x.PhysicalId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        }

        public static HandoffResult Run(IHandoffParticipant local, IHandoffParticipant remote, Func<bool> confirm)
        {
            bool localBegun = false, remoteBegun = false;
            HandoffResult result = new HandoffResult();
            try
            {
                localBegun = true; local.Begin();
                if (remote != null) { remoteBegun = true; remote.Begin(); }
                List<InputRequest> plan = CombinePlans(local.Plan(), remote == null ? new InputRequest[0] : remote.Plan());
                // Capture source inputs while their current Windows paths still exist.
                HashSet<string> captured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (InputRequest input in plan) if (TryRead(local, remote, input.PhysicalId, true, out uint ignored)) captured.Add(input.PhysicalId);
                local.Prepare(); if (remote != null) remote.Prepare();
                foreach (InputRequest input in plan)
                    if (!captured.Contains(input.PhysicalId) && !TryRead(local, remote, input.PhysicalId, true, out uint ignored))
                        throw new InvalidOperationException("Cannot read the previous input for monitor '" + input.PhysicalId + "'. No input will be changed without a recovery route.");
                // Originals were captured above; a fallback route must not overwrite
                // them with an input that a previous set command already changed.
                foreach (InputRequest input in plan) SetThroughEither(local, remote, input, false);
                foreach (InputRequest input in plan) VerifyThroughEither(local, remote, input);
                // Only now may the outgoing participant release its shared displays.
                local.Apply(); if (remote != null) remote.Apply();
                if (!confirm()) { Restore(local, remote, result.RecoveryErrors); return result; }
                if (remote != null) remote.Commit(); local.Commit(); result.Committed = true; return result;
            }
            catch (Exception error)
            {
                Restore(localBegun ? local : null, remoteBegun ? remote : null, result.RecoveryErrors);
                throw new InvalidOperationException(error.Message + (result.RecoveryErrors.Count == 0 ? "" : " Recovery needs attention: " + String.Join("; ", result.RecoveryErrors)), error);
            }
        }

        static bool TryRead(IHandoffParticipant first, IHandoffParticipant second, string id, bool capture, out uint value)
        {
            foreach (IHandoffParticipant participant in new[] { first, second })
            {
                if (participant == null) continue;
                try { value = participant.ReadInput(id, capture); return true; } catch (InvalidOperationException) { }
            }
            value = 0; return false;
        }
        static void VerifyThroughEither(IHandoffParticipant first, IHandoffParticipant second, InputRequest input)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                foreach (IHandoffParticipant participant in new[] { first, second })
                {
                    if (participant == null) continue;
                    try { if (participant.ReadInput(input.PhysicalId, false) == input.Input) return; } catch (InvalidOperationException) { }
                }
                if (attempt < 2) System.Threading.Thread.Sleep(250);
            }
            throw new InvalidOperationException("Monitor '" + input.PhysicalId + "' did not confirm input " + DdcEngine.InputName(input.Input) + ".");
        }
        static void SetThroughEither(IHandoffParticipant first, IHandoffParticipant second, InputRequest input, bool capture)
        {
            string lastError = "No monitor control route is available.";
            foreach (IHandoffParticipant participant in new[] { first, second })
            {
                if (participant == null) continue;
                try { participant.SetInput(input.PhysicalId, input.Input, capture); return; } catch (InvalidOperationException error) { lastError = error.Message; }
            }
            throw new InvalidOperationException("Cannot switch monitor '" + input.PhysicalId + "': " + lastError);
        }

        public static void Restore(IHandoffParticipant local, IHandoffParticipant remote, List<string> errors)
        {
            foreach (IHandoffParticipant participant in new[] { local, remote }) if (participant != null) Attempt(participant.PrepareRestore, errors);
            List<InputRequest> previous = new List<InputRequest>();
            foreach (IHandoffParticipant participant in new[] { local, remote }) if (participant != null) Attempt(delegate { previous.AddRange(participant.PreviousInputs()); }, errors);
            List<InputRequest> combined = null; Attempt(delegate { combined = CombinePlans(previous, new InputRequest[0]); }, errors);
            if (combined != null) foreach (InputRequest input in combined) Attempt(delegate { SetThroughEither(local, remote, input, false); VerifyThroughEither(local, remote, input); }, errors);
            // Try both layouts even if an input or the peer connection could not be recovered.
            foreach (IHandoffParticipant participant in new[] { local, remote }) if (participant != null) Attempt(participant.RestoreLayout, errors);
            foreach (IHandoffParticipant participant in new[] { local, remote }) if (participant != null) Attempt(participant.CompleteRestore, errors);
        }
        static void Attempt(Action action, List<string> errors) { try { action(); } catch (Exception error) { errors.Add(error.Message); } }
    }
}
