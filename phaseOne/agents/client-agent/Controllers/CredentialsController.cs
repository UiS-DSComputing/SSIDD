using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Hyperledger.Aries.Agents;
using Hyperledger.Aries.Configuration;
using Hyperledger.Aries.Extensions;
using Hyperledger.Aries.Features.Handshakes.DidExchange;
using Hyperledger.Aries.Features.IssueCredential;
using Hyperledger.Aries.Models.Records;
using Hyperledger.Aries.Features.Handshakes.Connection;
using Hyperledger.Aries.Storage;
using Hyperledger.Indy.DidApi;
using Hyperledger.Indy.LedgerApi;
using Newtonsoft.Json;
using WebAgent.Models;
using Hyperledger.Indy.AnonCredsApi;
using System.Linq;
using System.Diagnostics;
// using WebAgent.Messages;
namespace WebAgent.Controllers
{
    public class CredentialsController : Controller
    {
        private readonly IAgentProvider _agentContextProvider;
        private readonly IProvisioningService _provisionService;
        private readonly IWalletService _walletService;
        private readonly IConnectionService _connectionService;
        private readonly ICredentialService _credentialService;
        private readonly ISchemaService _schemaService;
        private readonly IMessageService _messageService;
        private static Random random = new Random();

        public CredentialsController(
            IAgentProvider agentContextProvider,
            IProvisioningService provisionService,
            IWalletService walletService,
            IConnectionService connectionService,
            ICredentialService credentialService,
            ISchemaService schemaService,
            IMessageService messageService)
        {
            _agentContextProvider = agentContextProvider;
            _provisionService = provisionService;
            _walletService = walletService;
            _connectionService = connectionService;
            _credentialService = credentialService;
            _schemaService = schemaService;
            _messageService = messageService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {

            var context = await _agentContextProvider.GetContextAsync();
            var credentials = await _credentialService.ListAsync(context);
            var models = new List<CredentialViewModel>();
            foreach (var c in credentials)
            {
                models.Add(new CredentialViewModel
                {
                    SchemaId = c.SchemaId,
                    CreatedAt = c.CreatedAtUtc ?? DateTime.MinValue,
                    State = c.State,
                    CredentialAttributesValues = c.CredentialAttributesValues
                });
            }
            return View(new CredentialsViewModel { Credentials = models });
        }


        [HttpGet]
        public async Task<IActionResult> RegisterSchema()
        {
            try
            {

                var context = await _agentContextProvider.GetContextAsync();
                var issuer = await _provisionService.GetProvisioningAsync(context.Wallet);
                var Trustee = await Did.CreateAndStoreMyDidAsync(context.Wallet,
                    new { seed = "000000000000000000000000Steward1" }.ToJson());
                await Ledger.SignAndSubmitRequestAsync(await context.Pool, context.Wallet, Trustee.Did,
                    await Ledger.BuildNymRequestAsync(Trustee.Did, issuer.IssuerDid, issuer.IssuerVerkey, null, "ENDORSER"));

                var schemaId = await _schemaService.CreateSchemaAsync(
                    context: context,
                    issuerDid: issuer.IssuerDid,
                    name: "organisation-schema",
                    version: "1.0",
                    attributeNames: new[] { "name", "organisationid", "status", "employmentstart", "expiration", "nftid", "timestamp" });
                await _schemaService.CreateCredentialDefinitionAsync(context, new CredentialDefinitionConfiguration
                {
                    SchemaId = schemaId,
                    Tag = "default",
                    EnableRevocation = false, // TODO: HANIF - enable this feature afterwards if anough time
                    RevocationRegistrySize = 0,
                    RevocationRegistryBaseUri = "",
                    RevocationRegistryAutoScale = false,
                    IssuerDid = issuer.IssuerDid
                });
            }
            catch (Exception e)
            {
                Console.WriteLine("\nException Caught!");
                Console.WriteLine("Message: {0} ", e.Message);
                var st = new StackTrace(e, true);
                var line = st.GetFrame(st.FrameCount - 1).GetFileLineNumber();
                Console.WriteLine("Line number: {0} ", line);
            }

            return RedirectToAction("CredentialsForm");
        }

        [HttpGet]
        public async Task<IActionResult> CredentialsForm()
        {
            var context = await _agentContextProvider.GetContextAsync();
            var model = new CredentialFormModel
            {
                Connections = await _connectionService.ListAsync(context),
                CredentialDefinitions = await _schemaService.ListCredentialDefinitionsAsync(context.Wallet),
                Schemas = await _schemaService.ListSchemasAsync(context.Wallet)
            };
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> IssueCredentials(CredentialOfferModel model)
        {
            var context = await _agentContextProvider.GetContextAsync();
            var issuer = await _provisionService.GetProvisioningAsync(context.Wallet);
            var connection = await _connectionService.GetAsync(context, model.ConnectionId);
            var values = JsonConvert.DeserializeObject<List<CredentialPreviewAttribute>>(model.CredentialAttributes);

            foreach (CredentialPreviewAttribute attr in values)
            {
                attr.MimeType = CredentialMimeTypes.ApplicationJsonMimeType;
            }

            var (offer, _) = await _credentialService.CreateOfferAsync(context, new OfferConfiguration
            {
                CredentialDefinitionId = model.CredentialDefinitionId,
                IssuerDid = issuer.IssuerDid,
                CredentialAttributeValues = values
            });
            await _messageService.SendAsync(context, offer, connection);

            return RedirectToAction("Index");
        }
        public static string RandomString(int min, int max)
        {
            return random.Next(min, max).ToString();
        }
    }
}
