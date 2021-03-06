﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartKioskBot.Controllers;
using SmartKioskBot.Helpers;
using SmartKioskBot.Logic;
using SmartKioskBot.Models;
using SmartKioskBot.UI;
using static SmartKioskBot.Helpers.Constants;
using static SmartKioskBot.Helpers.AdaptiveCardHelper;
using Newtonsoft.Json.Linq;

namespace SmartKioskBot.Dialogs
{
    [Serializable]
    public class CompareDialog : IDialog<Object>
    {
        public State state = State.INIT;
        public List<Product> products = new List<Product>();

        public enum State { INIT, INPUT_HANDLER };

        public CompareDialog(State state)
        {
            this.products = new List<Product>();
            this.state = state;
        }

        public async Task StartAsync(IDialogContext context)
        {
            switch (state)
            {
                case State.INIT:
                    await InitAsync(context, null);
                    break;
                case State.INPUT_HANDLER:
                    context.Wait(InputHandler);
                    break;
            }
        }

        public async Task InitAsync(IDialogContext context, IAwaitable<IMessageActivity> activity)
        {
            List<ButtonType> buttons = new List<ButtonType>();

            // fetch products
            var itemsToCompare = StateHelper.GetComparatorItems(context);
            
            foreach(string o in itemsToCompare)
                products.Add(ProductController.getProduct(o));

            var reply = context.MakeMessage();

            string intro_msg = "Bem-vindo ao comparador. Aqui posso dar-lhe sugestões sobre quais os melhores produtos que deseja comparar.";
            string button_msg = "";

            if (products.Count > 0)
            {

                intro_msg = "\nVou buscar os produtos em que demonstrou interesse em avaliar. Espere só um momento por favor.";
                await Interactions.SendMessage(context, intro_msg, 0, 0);

                //display products 
                reply = context.MakeMessage();
                reply.AttachmentLayout = AttachmentLayoutTypes.Carousel;
                List<Attachment> cards = new List<Attachment>();

                //limit 
                for (var i = 0; i < products.Count && i < Constants.N_ITEMS_CARROUSSEL; i++)
                    cards.Add(ProductCard.GetProductCard(products[i], ProductCard.CardType.COMPARATOR).ToAttachment());

                reply.Attachments = cards;

                await Interactions.SendMessage(context, "Aqui estão os produtos:", 4000, 2000);
                await context.PostAsync(reply);

                if (products.Count <= ComparatorLogic.MAX_PRODUCTS_ON_COMPARATOR)
                    buttons.Add(ButtonType.ADD_PRODUCT);

                buttons.Add(ButtonType.COMPARE);

                button_msg = "Se quiser posso fazer uma avaliação dos produtos, clique no botão abaixo para que eu iniciar a comparação.";
            }
            else
            {
                intro_msg = "\nDe momentos ainda não adicionou nenhum produto para ser avaliado.";
                await Interactions.SendMessage(context, intro_msg, 0, 3000);
                buttons.Add(ButtonType.ADD_PRODUCT);
            }

            button_msg += "\nSe tiver interesse em adicionar produtos para serem avaliados, faça uma pesquisa no nosso catálogo. Para isto, clique no botão abaixo para preencher um formulário de pesquisa.";
            await Interactions.SendMessage(context, button_msg, 3000, 2000);

            //show options
            reply = context.MakeMessage();
            reply.Attachments.Add(getCardButtonsAttachment(buttons, DialogType.COMPARE));
            await context.PostAsync(reply);

            context.Wait(InputHandler);
        }

        public async Task InputHandler(IDialogContext context, IAwaitable<object> argument)
        {
           var activity = await argument as Activity;

            //Received a Message
            if (activity.Text != null)
                context.Done(new CODE(DIALOG_CODE.PROCESS_LUIS, activity));
            //Received an Event
            else if (activity.Value != null)
                await EventHandler(context, activity);
            else
                context.Done(new CODE(DIALOG_CODE.DONE));
        }

        private async Task EventHandler(IDialogContext context, Activity activity)
        {
            JObject json = activity.Value as JObject;
            List<InputData> data = getReplyData(json);

            //have mandatory info
            if (data.Count >= 2)
            {
                //json structure is correct
                if (data[0].attribute == REPLY_ATR && data[1].attribute == DIALOG_ATR)
                {
                    ClickType event_click = getClickType(data[0].value);
                    DialogType event_dialog = getDialogType(data[1].value);

                    //event for this dialog
                    if (event_dialog == DialogType.COMPARE &&
                        event_click != ClickType.NONE)
                    {
                        switch (event_click)
                        {
                            case ClickType.COMPARE:
                                await Compare(context);
                                context.Wait(InputHandler);
                                break;
                            case ClickType.ADD_PRODUCT:
                                context.Call(new FilterDialog(FilterDialog.State.INIT), ResumeAfterDialogCall);
                                break;
                        }
                    }
                    // event not for this dialog
                    else
                        context.Done(new CODE(DIALOG_CODE.PROCESS_EVENT, activity, event_dialog));
                }
                else
                    context.Done(new CODE(DIALOG_CODE.DONE));
            }
            else
                context.Done(new CODE(DIALOG_CODE.DONE));
        }
        
        private async Task ResumeAfterDialogCall(IDialogContext context, IAwaitable<object> result)
        {
            CODE code = await result as CODE;

            //child dialog invoked an event of this dialog
            if (code.dialog == DialogType.COMPARE)
                await EventHandler(context, code.activity);
            else
                context.Done(code);
        }

        public async Task Compare(IDialogContext context)
        {
            // fetch products
            var itemsToCompare = StateHelper.GetComparatorItems(context);

            foreach(string o in itemsToCompare)
                products.Add(ProductController.getProduct(o.ToString()));

            if(products.Count > 0)
            {
                var reply = context.MakeMessage();
                reply.Text = Interactions.getOngoingComp();
                await context.PostAsync(reply);

                ComparatorLogic.ShowProductComparison(context, products);

                /*
                //show options
                if(products.Count <= ComparatorLogic.MAX_PRODUCTS_ON_COMPARATOR)
                {
                    reply = context.MakeMessage();
                    reply.Attachments.Add(getCardButtonsAttachment(
                        new List<ButtonType> { ButtonType.ADD_PRODUCT }, DialogType.COMPARE));
                    await context.PostAsync(reply);
                }*/
               
            }
            else
            {
                await context.PostAsync("Não tem produtos para comparar.");

                //show options
                var reply = context.MakeMessage();
                reply.Attachments.Add(getCardButtonsAttachment(
                    new List<ButtonType> { ButtonType.ADD_PRODUCT }, DialogType.COMPARE));
                await context.PostAsync(reply);
            }
            
        }

        public static async Task AddComparator(IDialogContext context, string message)
        {
            string[] parts = message.Split(':');
            var product = parts[1].Replace(" ", "");

            if (parts.Length >= 2)
            {
                List<string> items = StateHelper.GetComparatorItems(context);

                if (ComparatorLogic.MAX_PRODUCTS_ON_COMPARATOR <= items.Count)
                    await context.PostAsync("Lamento mas só consigo avaliar até " + 
                        ComparatorLogic.MAX_PRODUCTS_ON_COMPARATOR.ToString() + " produtos.");
                else
                {
                    Product productToAdd = ProductController.getProduct(product);
                    
                    var reply = context.MakeMessage();
                    reply.Text = String.Format(Interactions.getAddComparator());
                    await context.PostAsync(reply);

                    StateHelper.AddItemComparator(context, product);
                }
            }
        }

        public static async Task RmvComparator(IDialogContext _context, string message)
        {
            string[] parts = message.Split(':');
            var product = parts[1].Replace(" ", "");

            if (parts.Length >= 2)
            {
                var reply = _context.MakeMessage();
                reply.Text = Interactions.getRemComparator();
                await _context.PostAsync(reply);
                
                StateHelper.RemItemComparator(_context, product);
            }
        }

        
    }
}
