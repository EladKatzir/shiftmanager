# Support Escalation Path

## Escalation Matrix

| Level | Role | Capabilities | Typical Issues |
|-------|------|-------------|----------------|
| L1 | **User** | Self-service: view schedule, submit requests, check notifications | "I can't find my shift", "How do I swap?" |
| L2 | **Manager** | Manage team: assign shifts, approve requests, view analytics | Shift conflicts, scheduling questions, team issues |
| L3 | **Director** | Cross-company: view multiple companies, manage managers | Cross-team coordination, capacity planning |
| L4 | **Owner** | System admin: unlock users, manage grants, system health, backups | Account lockouts, system configuration, data issues |
| L5 | **Engineering** | Code-level: database access, debugging, hotfixes | Bugs, data corruption, migration issues, security incidents |

## Common Issues & Resolution

### "I can't log in"
1. **L1**: Check email spelling, try again
2. **L4**: Owner checks OwnerHub > Locked Users, unlocks if locked
3. **L4**: Owner resets password if forgotten (Admin > Users)

### "My shifts disappeared"
1. **L2**: Manager checks calendar view, verifies correct date range
2. **L3**: Director checks if company context was switched
3. **L4**: Owner checks audit log for assignment changes

### "Vacation request is stuck"
1. **L2**: Manager checks request status, approves if pending
2. **L4**: Owner checks if approver is deactivated, reassigns if needed
3. **L1**: User cancels stuck request and resubmits

### "Calendar shows wrong data"
1. **L1**: Refresh page (F5 or pull-to-refresh)
2. **L1**: Check connection indicator (yellow/red banner = disconnected)
3. **L4**: Owner checks System Health page for server issues

## Contact Information

Configure support contact in application settings:
- In-app: Help page links to support contact
- Default escalation: Contact your direct manager first
