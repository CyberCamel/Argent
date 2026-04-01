using Argent.Core.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Argent.Core.Identity;

public class Position
{
    public Guid Id { get; set; }
    public Guid PersonId {  get; set; }
    public required InternalUser Person { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate   { get; set; }
    public string? Title { get; set; }
    public string? Title2 { get; set; }
    public string? ManagerLevel { get; set; }
    public required Organization Organization { get; set; }
}
