using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PJATK_APBD_EFCOREDBFIRST.Data;
using PJATK_APBD_EFCOREDBFIRST.DTOs;
using PJATK_APBD_EFCOREDBFIRST.Models;

namespace PJATK_APBD_EFCOREDBFIRST.Controllers;

[ApiController]
[Route("api/patients")]
public class PatientsController : ControllerBase
{
    private readonly MasterContext _context;

    public PatientsController(MasterContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetPatients([FromQuery] string? search)
    {
        var query = _context.Patients
            .Include(p => p.Admissions)
                .ThenInclude(a => a.Ward)
            .Include(p => p.BedAssignments)
                .ThenInclude(ba => ba.Bed)
                    .ThenInclude(b => b.BedType)
            .Include(p => p.BedAssignments)
                .ThenInclude(ba => ba.Bed)
                    .ThenInclude(b => b.Room)
                        .ThenInclude(r => r.Ward)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string searchLower = search.ToLower();
            query = query.Where(p => p.FirstName.ToLower().Contains(searchLower) 
                                  || p.LastName.ToLower().Contains(searchLower));
        }

        var patients = await query.ToListAsync();

        var result = patients.Select(p => new PatientGetDto
        {
            Pesel = p.Pesel,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Age = p.Age,
            Sex = p.Sex ? "Male" : "Female",
            Admissions = p.Admissions.Select(a => new AdmissionDto
            {
                Id = a.Id,
                AdmissionDate = a.AdmissionDate,
                DischargeDate = a.DischargeDate,
                Ward = new WardDto
                {
                    Id = a.Ward.Id,
                    Name = a.Ward.Name,
                    Description = a.Ward.Description
                }
            }).ToList(),
            BedAssignments = p.BedAssignments.Select(ba => new BedAssignmentDto
            {
                Id = ba.Id,
                From = ba.From,
                To = ba.To,
                Bed = new BedDto
                {
                    Id = ba.Bed.Id,
                    BedType = new BedTypeDto
                    {
                        Id = ba.Bed.BedType.Id,
                        Name = ba.Bed.BedType.Name,
                        Description = ba.Bed.BedType.Description
                    },
                    Room = new RoomDto
                    {
                        Id = ba.Bed.Room.Id,
                        HasTv = ba.Bed.Room.HasTv,
                        Ward = new WardDto
                        {
                            Id = ba.Bed.Room.Ward.Id,
                            Name = ba.Bed.Room.Ward.Name,
                            Description = ba.Bed.Room.Ward.Description
                        }
                    }
                }
            }).ToList()
        }).ToList();

        return Ok(result);
    }

    [HttpPost("{pesel}/bedassignments")]
    public async Task<IActionResult> AssignBed(string pesel, [FromBody] AssignBedDto dto)
    {
        var patientExists = await _context.Patients.AnyAsync(p => p.Pesel == pesel);
        if (!patientExists)
        {
            return NotFound($"Pacjent o podanym numerze PESEL '{pesel}' nie istnieje w bazie danych.");
        }

        DateTime reqFrom = dto.From;
        DateTime reqTo = dto.To ?? DateTime.MaxValue;

        if (reqTo < reqFrom)
        {
            return BadRequest("Data zakończenia ('to') nie może być wcześniejsza niż data rozpoczęcia ('from').");
        }

        var potentialBeds = await _context.Beds
            .Include(b => b.BedType)
            .Include(b => b.Room)
                .ThenInclude(r => r.Ward)
            .Where(b => b.BedType.Name == dto.BedType && b.Room.Ward.Name == dto.Ward)
            .ToListAsync();

        if (!potentialBeds.Any())
        {
            return NotFound($"Nie znaleziono łóżek typu '{dto.BedType}' na oddziale '{dto.Ward}'.");
        }

        Bed? availableBed = null;

        foreach (var bed in potentialBeds)
        {
            var existingAssignments = await _context.BedAssignments
                .Where(ba => ba.BedId == bed.Id)
                .ToListAsync();

            bool isOverlapping = existingAssignments.Any(ba =>
            {
                DateTime activeFrom = ba.From;
                DateTime activeTo = ba.To ?? DateTime.MaxValue;

                return reqFrom < activeTo && reqTo > activeFrom;
            });

            if (!isOverlapping)
            {
                availableBed = bed;
                break;
            }
        }

        if (availableBed == null)
        {
            return NotFound($"Wszystkie łóżka typu '{dto.BedType}' na oddziale '{dto.Ward}' są zajęte w wybranym przedziale czasowym ({dto.From:yyyy-MM-dd HH:mm} - {(dto.To.HasValue ? dto.To.Value.ToString("yyyy-MM-dd HH:mm") : "nieokreślone")}).");
        }

        var newAssignment = new BedAssignment
        {
            PatientPesel = pesel,
            BedId = availableBed.Id,
            From = dto.From,
            To = dto.To
        };

        _context.BedAssignments.Add(newAssignment);
        await _context.SaveChangesAsync();

        return Created("", new { Message = $"Pomyślnie przypisano pacjenta do łóżka o ID {availableBed.Id} w pokoju {availableBed.RoomId}." });
    }
}