using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using App_Pokemon_Aaron.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using MimeKit;
using MailKit.Net.Smtp;
using ClosedXML.Excel;
using System.IO;

    public class PokemonController : Controller
    {
        private readonly HttpClient _httpClient;

        public PokemonController() { 
            _httpClient = new HttpClient { BaseAddress = new Uri("https://pokeapi.co/api/v2/")};
        }
        public async Task<IActionResult> Index(int page = 1, string name = null, string species = null)
        {
            int limit = 12; //cantidad de pokemones por pagina
            if(page < 1) page = 1;
            var pokemonList = new List<PokemonViewModel>();
            //para filtro select
            if (!string.IsNullOrEmpty(species))
            {
                //informacion de la especie
                var speciesResponse = await _httpClient.GetAsync($"pokemon-species/{species.ToLower()}");
                //checar la respuesta 
                if (!speciesResponse.IsSuccessStatusCode)
                {
                    ViewBag.CurrentPage = page;
                    ViewBag.TotalPage = 0;
                    ViewBag.SpeciesFilter = species;
                    ViewBag.SpeciesList = await GetSpeciesList();
                    return View(pokemonList);
                }

                var speciesContent = await speciesResponse.Content.ReadAsStringAsync();
                var speciesJson = JObject.Parse(speciesContent);
                var varieties = speciesJson["varieties"];

                var filteredNames = new List<string>();
                //organizacion por especie
                foreach (var variety in varieties)
                {
                    var pokemonName = variety["pokemon"]["name"].ToString();
                    if (string.IsNullOrEmpty(name) || pokemonName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        filteredNames.Add(pokemonName);
                    }
                }

                int totalItems = filteredNames.Count;
                int totalPages = (int)Math.Ceiling((double)totalItems / limit);
                var paginatedNames = filteredNames.Skip((page - 1) * limit).Take(limit).ToList();
                //pokemon de cada pagina
                foreach (var pokemonName in paginatedNames)
                {
                    var detailResponse = await _httpClient.GetAsync($"pokemon/{pokemonName}");
                    var detailContent = await detailResponse.Content.ReadAsStringAsync();
                    var detailJson = JObject.Parse(detailContent);
                    pokemonList.Add(new PokemonViewModel
                    { 
                        ImageUrl = detailJson["sprites"]["front_default"]?.ToString(),
                        Name = pokemonName
                    });

                }
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SpeciesFilter = species;
                ViewBag.NameFilter = name;
                ViewBag.SpeciesList = await GetSpeciesList();
                return View(pokemonList);
            }
            //para filtro input
            else
            {
                string url = "pokemon?limit=1100&offset=0"; //limite de pokemones
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                var results = json["results"];
                //filtro por nombre
                var filtered = results
                    .Where(p => string.IsNullOrEmpty(name) || p["name"].ToString().Contains(name , StringComparison.OrdinalIgnoreCase))
                    .ToList();
                int totalItems = filtered.Count;
                int totalPages = (int)Math.Ceiling((double)totalItems / limit);
                var paginated = filtered.Skip((page - 1) * limit).Take(limit);
                //datos de cada pokemon
                foreach (var item in paginated)
                {
                    string pokemonName = item["name"].ToString();
                    var detailResponse = await _httpClient.GetAsync($"pokemon/{pokemonName}");
                    var detailContent = await detailResponse.Content.ReadAsStringAsync();
                    var detailJson = JObject.Parse(detailContent);

                    pokemonList.Add(new PokemonViewModel
                    {
                        ImageUrl = detailJson["sprites"]["front_default"]?.ToString(),
                        Name = pokemonName
                    });
                }

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SpeciesFilter = species;
                ViewBag.NameFilter = name;
                ViewBag.SpeciesList = await GetSpeciesList();
                return View(pokemonList);
            }
        }

    private async Task<List<string>> GetSpeciesList()
        {
            var response = await _httpClient.GetAsync("pokemon-species?limit=1100");
            var content = await response.Content.ReadAsStringAsync();
            var json =  JObject.Parse(content);
            var results = json["results"];
            var speciesList = new List<string>();
            foreach (var item in results)
            {
                string speciesName = item["name"].ToString();
                speciesList.Add(speciesName);
            }
            return speciesList.OrderBy(s => s).ToList();
        }

        //descargar excel
        public async Task<IActionResult> DescargarExcel()
        {
            string url = $"pokemon?limit=1100&offset=0";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            var results = json["results"];

            var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Pokemones");
            worksheet.Cell(1, 1).Value = "Imagen";
            worksheet.Cell(1,2).Value = "Nombre";

            int row = 2;
            using var httpCliente = new HttpClient();
            foreach (var item in results)
            {
                string pokemonName = item["name"].ToString();
                var detailResponse = await _httpClient.GetAsync($"pokemon/{pokemonName}");
                var detailContent = await detailResponse.Content.ReadAsStringAsync();
                var detailJson = JObject.Parse(detailContent);
                string imageUrl = detailJson["sprites"]["front_default"]?.ToString();

                //descargar imagen en bites
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    try
                    {
                        var imageBytes = await httpCliente.GetByteArrayAsync(imageUrl);
                        using var ms = new MemoryStream(imageBytes);
                        //se agrega la imagen al excel
                        var img = worksheet.AddPicture(ms)
                        .MoveTo(worksheet.Cell(row, 1))
                        .Scale(0.2);
                    }
                    catch
                    {
                        worksheet.Cell(row, 1).Value = "No image";
                    }
                }
                else 
                {
                    worksheet.Cell(row, 1).Value = "No image";
                }
                worksheet.Cell(row, 2).Value = pokemonName;
                row++;
            }

            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                stream.Position = 0;
                return File(stream.ToArray(),
                    "application/vdn.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Pokemones.xlsx");
            }

        }

        //enviar correo con excel
        private async Task EnviarCorreoConExcel(MemoryStream excelStream, string destinatario) 
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress("Aaron", "correodepruebaaron01@gmail.com"));
            mensaje.To.Add(MailboxAddress.Parse(destinatario));
            mensaje.Subject = "Pokemones";
            var builder = new BodyBuilder
            {
                TextBody = "Te envio por medio de este mensaje un Excel con los Pokemones."
            };
            builder.Attachments.Add("Pokemones.xlsx",excelStream.ToArray(),
            new ContentType("application","vnd.openmxlformats-officedocument.spreadsheetml.sheet"));
            mensaje.Body = builder.ToMessageBody();
            
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync("correodepruebaaron01@gmail.com", "itso ukom cwhe ykpl");
            await smtp.SendAsync(mensaje);
            await smtp.DisconnectAsync(true);
        }
        public async Task<IActionResult> EnviarCorreo()
        {
            var stream = new MemoryStream();

        
            string url = $"pokemon?limit=1100&offset=0";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            var results = json["results"];

            var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Pokemones");
            worksheet.Cell(1, 1).Value = "Imagen";
            worksheet.Cell(1, 2).Value = "Nombre";

            int row = 2;
            using var httpCliente = new HttpClient();
            foreach (var item in results)
            {
                string pokemonName = item["name"].ToString();
                var detailResponse = await _httpClient.GetAsync($"pokemon/{pokemonName}");
                var detailContent = await detailResponse.Content.ReadAsStringAsync();
                var detailJson = JObject.Parse(detailContent);
                string imageUrl = detailJson["sprites"]["front_default"]?.ToString();

                //descargar imagen en bites
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    try
                    {
                        var imageBytes = await httpCliente.GetByteArrayAsync(imageUrl);
                        using var ms = new MemoryStream(imageBytes);
                        //se agrega la imagen al excel
                        var img = worksheet.AddPicture(ms)
                        .MoveTo(worksheet.Cell(row, 1))
                        .Scale(0.2);
                    }
                    catch
                    {
                        worksheet.Cell(row, 1).Value = "No image";
                    }
                }
                else
                {
                    worksheet.Cell(row, 1).Value = "No image";
                }
                worksheet.Cell(row, 2).Value = pokemonName;
                row++;
            }

            workbook.SaveAs(stream);
            stream.Position = 0;
            await EnviarCorreoConExcel(stream, "alejacor0806@gmail.com"); //correo a donde se va enviar el excel
            return Ok("Correo Enviado");
        }
        
        
    }

