import * as firebase from 'firebase';

const config = {
  apiKey: 'AIzaSyAA0bes7QeNtEQAqNFat0dQHnSL5i_2VkY',
  authDomain: 'photoapp-d13a5.firebaseapp.com',
  databaseURL: 'https://photoapp-d13a5.firebaseio.com',
  projectId: 'photoapp-d13a5',
  storageBucket: 'photoapp-d13a5.appspot.com',
  messagingSenderId: '637089823633',
};
firebase.initializeApp(config);


export const autenticacion = firebase.auth();
export const baseDeDatos = firebase.database();
