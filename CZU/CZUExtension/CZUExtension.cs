
using System;
using Microsoft.MetadirectoryServices;

namespace Mms_Metaverse
{
    /// <summary>
    /// Summary description for MVExtensionObject.
    /// </summary>
    public class MVExtensionObject : IMVSynchronization
    {
        public MVExtensionObject()
        {
            //
            // TODO: Add constructor logic here
            //
        }

        void IMVSynchronization.Initialize()
        {
            //
            // TODO: Add initialization logic here
            //
        }

        void IMVSynchronization.Terminate()
        {
            //
            // TODO: Add termination logic here
            //
        }

        void IMVSynchronization.Provision(MVEntry mventry)
        {
            ConnectedMA EIDMMA = mventry.ConnectedMAs["EIDMA"];
            switch (mventry.ObjectType.ToLower())
            {
                case "baseunit":
                    createBaseUnit(mventry, EIDMMA);
                    break;
                case "ppv":
                    createPPV(mventry, EIDMMA);
                    break;
                case "card":
                    createEidmaObject(mventry, EIDMMA, "Card");
                    break;
                case "person":
                    createPerson(mventry, EIDMMA);
                    break;
            }
        }
        bool IMVSynchronization.ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {
            //
            // TODO: Add MV deletion logic here
            //
            throw new EntryPointNotImplementedException();
        }

        private void createBaseUnit(MVEntry mventry, ConnectedMA EIDMMA)
        {
            if (EIDMMA.Connectors.Count == 0 && mventry["claBaseUnitCode"].IsPresent)
            {

                CSEntry csEntry = EIDMMA.Connectors.StartNewConnector("BaseUnit");
                string strGUID = Guid.NewGuid().ToString().ToUpper();
                csEntry.DN = EIDMMA.CreateDN(strGUID);
                csEntry["idmObjectId"].StringValue = strGUID;
                csEntry["IsActive"].BooleanValue = true;
                csEntry["techName"].StringValue = mventry["claBaseUnitCode"].StringValue;
                csEntry.CommitNewConnector();
            }
        }
        private void createEidmaObject(MVEntry mventry, ConnectedMA EIDMMA,string ObjectType)
        {
            if (EIDMMA.Connectors.Count == 0 )
            {

                CSEntry csEntry = EIDMMA.Connectors.StartNewConnector(ObjectType);
                string strGUID = Guid.NewGuid().ToString().ToUpper();
                csEntry.DN = EIDMMA.CreateDN(strGUID);
                csEntry["idmObjectId"].StringValue = strGUID;
                csEntry.CommitNewConnector();
            }

        }
        private void createPPV(MVEntry mventry, ConnectedMA EIDMMA)
        {
            if (EIDMMA.Connectors.Count == 0 && mventry["PK_PPV"].IsPresent)
            {

                CSEntry csEntry = EIDMMA.Connectors.StartNewConnector("PPV");
                string strGUID = Guid.NewGuid().ToString().ToUpper();
                csEntry.DN = EIDMMA.CreateDN(strGUID);
                csEntry["idmObjectId"].StringValue = strGUID;
                csEntry["IsActive"].BooleanValue = true;
                csEntry["techName"].StringValue = mventry["PK_PPV"].StringValue;
                csEntry.CommitNewConnector();
            }

        }
        private void createPerson(MVEntry mventry, ConnectedMA EIDMMA)
        {
            if (EIDMMA.Connectors.Count == 0 && mventry["PK_ZAM"].IsPresent)
            {

                CSEntry csEntry = EIDMMA.Connectors.StartNewConnector("Identity");
                string strGUID = Guid.NewGuid().ToString().ToUpper();
                csEntry.DN = EIDMMA.CreateDN(strGUID);
                csEntry["idmObjectId"].StringValue = strGUID;
                csEntry["IsActive"].BooleanValue = true;
                
                //csEntry["techName"].StringValue = mventry["PK_ZAM"].Value.ToString();
                csEntry["iDMObjectTypeId"].StringValue = "4";
                csEntry["placedAd"].BooleanValue = true;
                csEntry["domain"].StringValue = "302";
                //domain 
                csEntry.CommitNewConnector();
            }

        }

    }
}
