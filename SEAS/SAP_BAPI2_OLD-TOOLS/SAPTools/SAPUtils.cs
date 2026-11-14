using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAPLibrary
{
    public class SAPUtils
    {

        public SAPUtils()
        {
            var cfg = new Configuration();
            List<Person> persons = new List<Person>();
            RfcDestinationManager.RegisterDestinationConfiguration(cfg);

        }

        public List<Person> GetAll()
        {
            List<Person> persons = new List<Person>();

            var dt = new Employees().GetAll();
            Console.WriteLine(dt.Rows.Count);
            foreach (DataRow row in dt.Rows)
            {
                Person person = new Person(row["USERNAME"].ToString());
                persons.Add(person);
            }
            return persons;
        }
    }
}