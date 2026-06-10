// Project-wide alias: xUnit ships a `Xunit.Record` helper that collides with our
// domain entity `ArchiveAgent.Core.Domain.Record`. The alias makes `Record` unambiguous.
global using Record = ArchiveAgent.Core.Domain.Record;
