using System.Collections.Concurrent;
namespace BusinessPartnerPortal.Api.Services;
public sealed record RegistrationChallenge(Guid Id,string SupplierNumber,string VendorId,string VendorName,string Email,string Otp,DateTimeOffset ExpiresAt,int Attempts=0);
public sealed class RegistrationOtpStore{readonly ConcurrentDictionary<Guid,RegistrationChallenge> data=new();public RegistrationChallenge Create(string supplierNumber,string vendorId,string vendorName,string email,string otp){var c=new RegistrationChallenge(Guid.NewGuid(),supplierNumber,vendorId,vendorName,email,otp,DateTimeOffset.UtcNow.AddMinutes(10));data[c.Id]=c;return c;}public RegistrationChallenge? Get(Guid id)=>data.TryGetValue(id,out var c)?c:null;public void Remove(Guid id)=>data.TryRemove(id,out _);}
