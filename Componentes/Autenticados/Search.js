import React, { Component } from 'react';
import { View, Text, Button } from 'react-native';

export default class Search extends Component {
  render() {
    const { navigation } = this.props;
    return (
      <View style={styles.container}>
        <Text> Search</Text>
        <Button title="Publicacion" onPress={() => { navigation.navigate('Publicacion'); }} />
      </View>
    );
  }
}

const styles = ({
  container: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: '#2c3e50',
  },
});
