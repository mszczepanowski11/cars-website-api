using System;
using System.ComponentModel.DataAnnotations;

namespace CarsWebsite
{
    public class User
    
    {  public int Id { get; set; }
       public string Name { get; set; }
       public string Surname { get; set; }
       public string Email { get; set; }
       public DateTime DateOfBirth { get; set; }
       public string PhoneNumber { get; set; }
       public string PasswordHash { get; set; }
    } 
}
