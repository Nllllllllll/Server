using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

// Supprimer divers avertissements, puisque c’est un fichier d’exemple.
#pragma warning disable CS8618 // Le champ non nullable doit contenir une valeur non nulle à la sortie du constructeur. Envisagez de le déclarer comme nullable.
#pragma warning disable IDE0060 // Supprimer le paramètre inutilisé
#pragma warning disable CS1998 // La méthode asynchrone ne contient aucun opérateur « await » et s’exécutera de manière synchrone

/*
 * Méthodes utiles à appeler dans les fonctions : GetRole(), GetGameServer(), GetPlayer()
 * Pour afficher un objet dynamique : DynamicHelper.ToString(exampleData)
 */
namespace PersistenceServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExampleController : BaseMmoController
    {
        // Exemple de requête 1 :
        // Cet exemple reçoit un body et lit son JSON dans un objet dynamique
        [HttpPost("create1")] // POST : api/example/create1
        [Authorize(Role.Client, Role.Server, Role.Anonymous)]
        public async Task<IActionResult> CreateExample1()
        {            
            var tcs = new TaskCompletionSource<IActionResult>();

            dynamic? exampleData = await GetBodyJsonAsync();
            if (exampleData == null)
            {
                tcs.SetResult(BadRequest());
                return await tcs.Task;
            }
            Console.WriteLine(DynamicHelper.ToString(exampleData));

            MmoWsServer.Singleton!.Processor.ConQ.Enqueue(async () =>
            {
                Console.WriteLine($"Génial. Exemple de nom de caractère: {exampleData.charName}");
                tcs.SetResult(Ok(exampleData));
            });

            return await tcs.Task;
        }

        // Exemple de requête 2 :
        // Cet exemple reçoit un body et lit son JSON dans un objet d’une classe C# (comme paramètre de la fonction)
        [HttpPost("create2")] // POST: api/example/create2
        [Authorize(Role.Client, Role.Server, Role.Anonymous)]
        public async Task<IActionResult> CreateExample2([FromBody] ExampleData exampleData)
        {
            var tcs = new TaskCompletionSource<IActionResult>();
            if (exampleData == null)
            {
                tcs.SetResult(BadRequest());
                return await tcs.Task;
            }

            MmoWsServer.Singleton!.Processor.ConQ.Enqueue(async () =>
            {
                Console.WriteLine("awesome");
                tcs.SetResult(Ok(exampleData));
            });

            return await tcs.Task;
        }

        // Exemple de requête GET
        // Cet exemple explore différentes façons de répondre à une requête
        [HttpGet("{id:int}")] // GET: api/example/{id}
        [Authorize(Role.Client, Role.Server, Role.Anonymous)]
        public Task<IActionResult> GetExample(int id)
        {
            var tcs = new TaskCompletionSource<IActionResult>();
            Role validatedRole = GetRole();            

            MmoWsServer.Singleton!.Processor.ConQ.Enqueue(async () => {
                //await Task.Delay(2000); // 2000 milliseconds = 2 seconds
                //var test = await MmoWsServer.Singleton!.Database.GetCharacter(4, 1);

                // Exemple fonctionnel 1 : données dans un objet d’une classe C# spécifique
                //var response = new ExampleData();
                //response.Id = 12;
                //response.Name = "tert";
                //tcs.SetResult(Ok(response));

                // working example 2: anonymous object
                //var response = new
                //{
                //    Id = 1,
                //    MemberName = "test",
                //    GuildRank = 2,
                //    Online = false
                //};
                //tcs.SetResult(Ok(response));

                // Exemple fonctionnel 3 : objet dynamique
                dynamic response = new ExpandoObject();
                response.Id = 1;
                response.MemberName = "test 3";
                response.GuildRank = 2;
                response.Online = false;
                tcs.SetResult(Ok(response));

                // Exemple fonctionnel 4 : simplement une chaîne
                //var response = "Example";
                //tcs.SetResult(Ok(response));

                // Exemple fonctionnel 5 : rejet avec un code d’état
                // tcs.SetResult(BadRequest());
                // Other potentially useful status codes:
                // BadRequest() returns json with status code 400
                // Unauthorized() returns json with status code 401
                // NotFound() returns json with status code 404                                
                // Conflict() returns json with status code 409               
            });

            return tcs.Task;
        }
    }

    public class ExampleData
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
#pragma warning restore CS1998 // La méthode asynchrone ne contient aucun opérateur 'await' et s’exécutera de manière synchrone
#pragma warning restore IDE0060 // Supprimer le paramètre inutilisé
#pragma warning restore CS8618 // Le champ non nullable doit contenir une valeur non nulle à la sortie du constructeur. Envisagez de le déclarer comme nullable.
