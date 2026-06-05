using Argent.Models.Identity.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Argent.Models.Identity;

public class Organization
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
    public string? OuFullPath { get; set; }
    public DateOnly StartDate;
    public DateOnly EndDate;
    public Person? Manager;
    public OrganizationType Type;

}
